using System.Threading.Channels;
using RinhaBackend.Net.Helper;
using RinhaBackend.Net.Infrastructure.Clients;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Payloads;

namespace RinhaBackend.Net.Services;

public class PaymentProcessorWorker(
    IPaymentQueueService queueService,
    IServiceProvider serviceProvider,
    ILogger<PaymentProcessorWorker> logger,
    IHttpClientFactory httpClientFactory)
    : BackgroundService
{
    private readonly Channel<PaymentPayload> _channel = Channel.CreateBounded<PaymentPayload>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });
    
    private readonly SemaphoreSlim _semaphore = new(Environment.ProcessorCount * 4);
    
    private readonly AtomicBoolean _defaultOk = new(true);
    private readonly AtomicBoolean _fallbackOk = new(true);
    private readonly AtomicCounter _defaultFailures = new();
    private readonly AtomicCounter _fallbackFailures = new();
    
    private const int MaxFailures = 3;
    private const int BaseDelayMs = 5;
    private const int MaxDelayMs = 100;
    
    private readonly PaymentProcessor _defaultClient = new(httpClientFactory.CreateClient("PaymentProcessorDefault"));
    private readonly PaymentProcessor _fallbackClient = new(httpClientFactory.CreateClient("PaymentProcessorFallback"));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PaymentProcessorWorker started with {ProcessorCount} processors", Environment.ProcessorCount);
        
        var tasks = new List<Task>();
        
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            tasks.Add(ProcessChannelAsync(stoppingToken));
        }
        
        tasks.Add(ConsumeExternalQueueAsync(stoppingToken));
        
        await Task.WhenAll(tasks);
        
        logger.LogInformation("PaymentProcessorWorker stopped");
    }

    private async Task ConsumeExternalQueueAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_defaultOk.Value || _fallbackOk.Value)
                {
                    if (await queueService.WaitToReadAsync(stoppingToken))
                    {
                        var payload = await queueService.DequeueAsync(stoppingToken);
                        if (payload is not null)
                        {
                            await _channel.Writer.WriteAsync(payload, stoppingToken);
                        }
                    }
                }
                else
                {
                    await Task.Delay(1, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error consuming external queue");
                await Task.Delay(1, stoppingToken);
            }
        }
        
        _channel.Writer.Complete();
    }

    private async Task ProcessChannelAsync(CancellationToken stoppingToken)
    {
        await foreach (var payload in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken);
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessPayload(payload, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Critical error processing payload {CorrelationId}", payload.CorrelationId);
                    if (payload.ShouldRetry)
                    {
                        var retryPayload = payload.IncrementRetry();
                        queueService.Enqueue(retryPayload);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }, stoppingToken);
        }
    }

    private async Task ProcessPayload(PaymentPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Processing payment {CorrelationId} with amount {Amount}", payload.CorrelationId, payload.Amount);
            
            var processor = await TryProcessWithResilience(payload, cancellationToken);
            logger.LogDebug("Processor result for {CorrelationId}: {Processor}", payload.CorrelationId, processor);
            
            if (processor is not null)
            {
                using var scope = serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<PaymentRepository>();
                
                var inserted = await repository.InsertAsync(payload, processor.Value);
                if (inserted)
                {
                    logger.LogInformation("Payment processed successfully with {Processor} for {CorrelationId}", processor, payload.CorrelationId);
                }
                else
                {
                    logger.LogInformation("Payment {CorrelationId} was already processed, skipping", payload.CorrelationId);
                }
            }
            else
            {
                logger.LogWarning("No processor available for payment {CorrelationId}, attempting to insert with fallback processor", payload.CorrelationId);
                
                using var scope = serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<PaymentRepository>();
                
                var inserted = await repository.InsertAsync(payload, ProcessorType.Fallback);
                if (inserted)
                {
                    logger.LogInformation("Payment inserted with fallback processor for {CorrelationId}", payload.CorrelationId);
                }
                else
                {
                    logger.LogInformation("Payment {CorrelationId} was already processed, skipping", payload.CorrelationId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing payload {CorrelationId}", payload.CorrelationId);
            if (payload.ShouldRetry)
            {
                var retryPayload = payload.IncrementRetry();
                logger.LogWarning("Re-queueing payment {CorrelationId} for retry (attempt {RetryCount}/{MaxRetries})", 
                    payload.CorrelationId, retryPayload.RetryCount, PaymentPayload.MaxRetries);
                queueService.Enqueue(retryPayload);
            }
            else
            {
                logger.LogError("Payment {CorrelationId} failed after {MaxRetries} attempts, giving up", 
                    payload.CorrelationId, PaymentPayload.MaxRetries);
            }
        }
    }

    private async Task<ProcessorType?> TryProcessWithResilience(PaymentPayload payload, CancellationToken cancellationToken)
    {
        var maxRetries = 3;
        var retryCount = 0;

        while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
        {
            var processor = await TryProcessOnce(payload, cancellationToken);
            if (processor is not null)
            {
                return processor;
            }

            retryCount++;
            if (retryCount < maxRetries)
            {
                var delay = Math.Min(BaseDelayMs * (int)Math.Pow(2, retryCount - 1), MaxDelayMs);
                logger.LogDebug("Retrying payment {CorrelationId} in {Delay}ms (attempt {RetryCount}/{MaxRetries})", 
                    payload.CorrelationId, delay, retryCount, maxRetries);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return null;
    }

    private async Task<ProcessorType?> TryProcessOnce(PaymentPayload payload, CancellationToken cancellationToken)
    {
        if (_defaultOk.Value)
        {
            try
            {
                logger.LogDebug("Attempting to process payment {CorrelationId} with default processor", payload.CorrelationId);
                var result = await _defaultClient.ProcessAsync(payload);
                logger.LogDebug("Default processor result for {CorrelationId}: {Result}", payload.CorrelationId, result);
                
                if (result)
                {
                    _defaultFailures.Reset();
                    logger.LogInformation("Default processor succeeded for {CorrelationId}", payload.CorrelationId);
                    return ProcessorType.Default;
                }

                logger.LogWarning("Default processor returned false for {CorrelationId}", payload.CorrelationId);
                await HandleProcessorFailure(_defaultClient, _defaultOk, _defaultFailures, "Default", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Default processor failed for {CorrelationId}", payload.CorrelationId);
                await HandleProcessorFailure(_defaultClient, _defaultOk, _defaultFailures, "Default", cancellationToken);
            }
        }
        else
        {
            logger.LogDebug("Default processor is marked as unhealthy, skipping for {CorrelationId}", payload.CorrelationId);
        }

        if (_fallbackOk.Value)
        {
            try
            {
                logger.LogDebug("Attempting to process payment {CorrelationId} with fallback processor", payload.CorrelationId);
                var result = await _fallbackClient.ProcessAsync(payload);
                logger.LogDebug("Fallback processor result for {CorrelationId}: {Result}", payload.CorrelationId, result);
                
                if (result)
                {
                    _fallbackFailures.Reset();
                    logger.LogInformation("Fallback processor succeeded for {CorrelationId}", payload.CorrelationId);
                    return ProcessorType.Fallback;
                }

                logger.LogWarning("Fallback processor returned false for {CorrelationId}", payload.CorrelationId);
                await HandleProcessorFailure(_fallbackClient, _fallbackOk, _fallbackFailures, "Fallback", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallback processor failed for {CorrelationId}", payload.CorrelationId);
                await HandleProcessorFailure(_fallbackClient, _fallbackOk, _fallbackFailures, "Fallback", cancellationToken);
            }
        }
        else
        {
            logger.LogDebug("Fallback processor is marked as unhealthy, skipping for {CorrelationId}", payload.CorrelationId);
        }

        if (!_defaultOk.Value && !_fallbackOk.Value)
        {
            logger.LogWarning("Both processors are unhealthy for {CorrelationId}, waiting for recovery", payload.CorrelationId);
            await WaitForHealthyProcessor(cancellationToken);
        }

        return null;
    }

    private async Task HandleProcessorFailure(
        PaymentProcessor client, 
        AtomicBoolean okFlag, 
        AtomicCounter failureCounter, 
        string processorName, 
        CancellationToken cancellationToken)
    {
        okFlag.Value = false;
        failureCounter.Next();

        logger.LogWarning("{Processor} processor marked as unhealthy (failures: {Failures})", 
            processorName, failureCounter.Current);

        if (failureCounter.Current >= MaxFailures)
        {
            var healthCheck = await client.HealthCheckAsync();
            var recoveryDelay = healthCheck?.MinResponseTime ?? BaseDelayMs;
            
            logger.LogInformation("{Processor} processor will be retried in {Delay}ms", 
                processorName, recoveryDelay);

            _ = Task.Run(async () =>
            {
                await Task.Delay(recoveryDelay, cancellationToken);
                okFlag.Value = true;
                failureCounter.Reset();
                logger.LogInformation("{Processor} processor marked as healthy again", processorName);
            }, cancellationToken);
        }
        else
        {
            var delay = Math.Min(BaseDelayMs * failureCounter.Current, MaxDelayMs);
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay, cancellationToken);
                okFlag.Value = true;
                logger.LogDebug("{Processor} processor retry enabled after {Delay}ms", processorName, delay);
            }, cancellationToken);
        }
    }

    private async Task WaitForHealthyProcessor(CancellationToken cancellationToken)
    {
        logger.LogWarning("Both processors are unhealthy, waiting for recovery...");
        
        while (!_defaultOk.Value && !_fallbackOk.Value && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(0, cancellationToken);
            
            try
            {
                var defaultHealth = await _defaultClient.HealthCheckAsync();
                var fallbackHealth = await _fallbackClient.HealthCheckAsync();

                if (defaultHealth is not null)
                {
                    _defaultOk.Value = true;
                    _defaultFailures.Reset();
                    logger.LogInformation("Default processor recovered");
                    break;
                }

                if (fallbackHealth is not null)
                {
                    _fallbackOk.Value = true;
                    _fallbackFailures.Reset();
                    logger.LogInformation("Fallback processor recovered");
                    break;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Health check failed during recovery wait");
            }
        }
    }
} 