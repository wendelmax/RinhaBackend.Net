using System.Threading.Channels;
using RinhaBackend.Net.Helper;
using RinhaBackend.Net.Infrastructure.Clients;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Models.Entities;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Payloads;

namespace RinhaBackend.Net.Services;

public class ProcessorService(
    IHttpClientFactory httpClientFactory,
    PaymentRepository repository)
    : BackgroundService
{
    private readonly Channel<PaymentPayload> _channel = Channel.CreateUnbounded<PaymentPayload>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly AtomicBoolean _defaultOk = new(true);
    private readonly AtomicBoolean _fallbackOk = new(true);
    private readonly AtomicCounter _counter = new();
    private const int MaxConcurrency = 50;
    private readonly PaymentProcessor _defaultClient = new PaymentProcessor(httpClientFactory.CreateClient("PaymentProcessorDefault"));
    private readonly PaymentProcessor _fallbackClient = new PaymentProcessor(httpClientFactory.CreateClient("PaymentProcessorFallback"));

    public void Enqueue(PaymentPayload payload) => _channel.Writer.TryWrite(payload);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_counter.Current < MaxConcurrency &&
                (_defaultOk.Value || _fallbackOk.Value) &&
                await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                if (_channel.Reader.TryRead(out var payload))
                {
                    _counter.Next();
                    _ = Task.Run(() => ProcessPayload(payload), stoppingToken)
                        .ContinueWith(_ => _counter.Reset(), stoppingToken);
                }
            }
        }
    }

    private async Task ProcessPayload(PaymentPayload payload)
    {
        var processor = await TryProcessWithResilience(payload);
        
        if (processor is not null)
        {
            var payment = new Payment
            {
                CorrelationId = payload.CorrelationId,
                Amount = payload.Amount,
                Processor = processor.Value
            };

            await repository.InsertAsync(payment);
        }
        else
        {
            Enqueue(payload); 
        }
    }

    private async Task<ProcessorType?> TryProcessWithResilience(PaymentPayload payload)
    {
        if (_defaultOk.Value)
        {
            try
            {
                var result = await _defaultClient.ProcessAsync(payload);
                if (result) return ProcessorType.Default;

                _defaultOk.Value = false;
            }
            catch
            {
                _defaultOk.Value = false;
            }
        }

        if (_fallbackOk.Value)
        {
            try
            {
                var result = await _fallbackClient.ProcessAsync(payload);
                if (result) return ProcessorType.Fallback;

                _fallbackOk.Value = false;
            }
            catch
            {
                _fallbackOk.Value = false;
            }
        }

        while (true)
        {
            var healthDefault = await _defaultClient.HealthCheckAsync();
            var healthFallback = await _fallbackClient.HealthCheckAsync();
            
            if (healthDefault is not null)
            {
                _defaultOk.Value = true;
                return await TryProcessWithResilience(payload);
            }
            
            if (healthFallback is not null)
            {
                _fallbackOk.Value = true;
                return await TryProcessWithResilience(payload);
            }
            
            await Task.Delay(5000);
        }
    }
} 