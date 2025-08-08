using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
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
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory)
    : BackgroundService
{
    private readonly Channel<PaymentPayload> _channel = Channel.CreateBounded<PaymentPayload>(new BoundedChannelOptions(2048)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });
    
    private readonly SemaphoreSlim _semaphore = new(Math.Max(8, Environment.ProcessorCount * 4));
    
    private readonly AtomicBoolean _defaultOk = new(true);
    private readonly AtomicBoolean _fallbackOk = new(true);
    private readonly AtomicCounter _defaultFailures = new();
    private readonly AtomicCounter _fallbackFailures = new();
    
    // Health check rate limiting - 5 seconds between calls
    private readonly AtomicBoolean _defaultHealthCheckAvailable = new(true);
    private readonly AtomicBoolean _fallbackHealthCheckAvailable = new(true);
    private DateTime _lastDefaultHealthCheck = DateTime.MinValue;
    private DateTime _lastFallbackHealthCheck = DateTime.MinValue;
    private readonly object _healthCheckLock = new();
    
    private const int MaxFailures = 5;
    private const int BaseDelayMs = 100;
    private const int MaxDelayMs = 2000;
    private const int HealthCheckCooldownMs = 5000; // 5 seconds
    
    private readonly PaymentProcessor _defaultClient = new(httpClientFactory.CreateClient("PaymentProcessorDefault"),
        loggerFactory.CreateLogger<PaymentProcessor>());
    private readonly PaymentProcessor _fallbackClient = new(httpClientFactory.CreateClient("PaymentProcessorFallback"),
        loggerFactory.CreateLogger<PaymentProcessor>());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        
        
        var tasks = new List<Task>();
        
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            tasks.Add(ProcessChannelAsync(stoppingToken));
        }
        
        tasks.Add(ConsumeExternalQueueAsync(stoppingToken));
        
        await Task.WhenAll(tasks);
        
        
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
                    await Task.Delay(5, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                
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
        }
    }

    private async Task ProcessPayload(PaymentPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            
            
            var processor = await TryProcessWithResilience(payload, cancellationToken);
            
            
            if (processor is not null)
            {
                using var scope = serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<PaymentRepository>();
                
                var inserted = await repository.InsertAsync(payload, processor.Value);
                if (inserted)
                {
                    
                }
                else
                {
                    
                }
            }
            else
            {
                
                
                using var scope = serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<PaymentRepository>();
                
                var inserted = await repository.InsertAsync(payload, ProcessorType.Fallback);
                if (inserted)
                {
                    
                }
                else
                {
                    
                }
            }
        }
        catch (Exception ex)
        {
            
            if (payload.ShouldRetry)
            {
                var retryPayload = payload.IncrementRetry();
                
                queueService.Enqueue(retryPayload);
            }
            else
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var repository = scope.ServiceProvider.GetRequiredService<PaymentRepository>();
                    await repository.InsertAsync(payload, ProcessorType.Fallback);
                }
                catch
                {
                    // swallow as we are in a best-effort fallback on final retry
                }
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
                
                var result = await _defaultClient.ProcessAsync(payload);
                
                
                if (result)
                {
                    _defaultFailures.Reset();
                    
                    return ProcessorType.Default;
                }

                
                await HandleProcessorFailure(_defaultClient, _defaultOk, _defaultFailures, "Default", cancellationToken);
            }
            catch (Exception ex)
            {
                
                await HandleProcessorFailure(_defaultClient, _defaultOk, _defaultFailures, "Default", cancellationToken);
            }
        }
        else
        {
            
        }

        if (_fallbackOk.Value)
        {
            try
            {
                
                var result = await _fallbackClient.ProcessAsync(payload);
                
                
                if (result)
                {
                    _fallbackFailures.Reset();
                    
                    return ProcessorType.Fallback;
                }

                
                await HandleProcessorFailure(_fallbackClient, _fallbackOk, _fallbackFailures, "Fallback", cancellationToken);
            }
            catch (Exception ex)
            {
                
                await HandleProcessorFailure(_fallbackClient, _fallbackOk, _fallbackFailures, "Fallback", cancellationToken);
            }
        }
        else
        {
            
        }

        if (!_defaultOk.Value && !_fallbackOk.Value)
        {
            
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
        failureCounter.Next();
        logger.LogWarning("{Processor} processor failure (failures: {Failures}/{MaxFailures})", 
            processorName, failureCounter.Current, MaxFailures);

        if (failureCounter.Current >= MaxFailures)
        {
            okFlag.Value = false;
            logger.LogWarning("{Processor} processor marked as unhealthy", processorName);
            
            // Use fixed recovery delay instead of health check to avoid blacklist
            var recoveryDelay = MaxDelayMs * 2; // 4 seconds
            
            logger.LogInformation("{Processor} processor will be retried in {Delay}ms (avoiding health check)", 
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
            // Exponential backoff for transient failures
            var delay = Math.Min(BaseDelayMs * (int)Math.Pow(2, failureCounter.Current - 1), MaxDelayMs);
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
        
        
        while (!_defaultOk.Value && !_fallbackOk.Value && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(HealthCheckCooldownMs, cancellationToken);
            
            try
            {
                // Check if we can perform health checks (respecting 5-second cooldown)
                lock (_healthCheckLock)
                {
                    var now = DateTime.UtcNow;
                    
                    if (_defaultHealthCheckAvailable.Value && 
                        (now - _lastDefaultHealthCheck).TotalMilliseconds >= HealthCheckCooldownMs)
                    {
                        _defaultHealthCheckAvailable.Value = false;
                        _lastDefaultHealthCheck = now;
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var defaultHealth = await _defaultClient.HealthCheckAsync();
                                if (defaultHealth is not null)
                                {
                                    _defaultOk.Value = true;
                                    _defaultFailures.Reset();
                                }
                            }
                            catch (Exception ex)
                            {
                                
                            }
                            finally
                            {
                                await Task.Delay(HealthCheckCooldownMs, cancellationToken);
                                _defaultHealthCheckAvailable.Value = true;
                            }
                        }, cancellationToken);
                    }
                    
                    if (_fallbackHealthCheckAvailable.Value && 
                        (now - _lastFallbackHealthCheck).TotalMilliseconds >= HealthCheckCooldownMs)
                    {
                        _fallbackHealthCheckAvailable.Value = false;
                        _lastFallbackHealthCheck = now;
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var fallbackHealth = await _fallbackClient.HealthCheckAsync();
                                if (fallbackHealth is not null)
                                {
                                    _fallbackOk.Value = true;
                                    _fallbackFailures.Reset();
                                }
                            }
                            catch (Exception ex)
                            {
                                
                            }
                            finally
                            {
                                await Task.Delay(HealthCheckCooldownMs, cancellationToken);
                                _fallbackHealthCheckAvailable.Value = true;
                            }
                        }, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                
            }
        }
    }
    

} 