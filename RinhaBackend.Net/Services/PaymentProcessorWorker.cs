using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
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
                if (await queueService.WaitToReadAsync(stoppingToken))
                {
                    var payload = await queueService.DequeueAsync(stoppingToken);
                    if (payload is not null)
                    {
                        await _channel.Writer.WriteAsync(payload, stoppingToken);
                    }
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
                }
            }
        }
    }

    private async Task<ProcessorType?> TryProcessWithResilience(PaymentPayload payload, CancellationToken cancellationToken)
    {
        var maxRetries = 2;
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
                await Task.Delay(50, cancellationToken);
            }
        }

        return null;
    }

    private async Task<ProcessorType?> TryProcessOnce(PaymentPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            var resultDefault = await _defaultClient.ProcessAsync(payload);
            if (resultDefault)
            {
                return ProcessorType.Default;
            }
        }
        catch
        {
        }

        try
        {
            var resultFallback = await _fallbackClient.ProcessAsync(payload);
            if (resultFallback)
            {
                return ProcessorType.Fallback;
            }
        }
        catch
        {
        }

        return null;
    }
}