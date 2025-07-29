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
                        .ContinueWith(_ => _counter.Reset());
                }
            }

            await Task.Delay(50, stoppingToken);
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
                RequestedAt = payload.RequestedAt,
                Status = PaymentStatus.PROCESSED,
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
                var defaultClient = new PaymentProcessor(httpClientFactory.CreateClient("PaymentProcessorDefault"));
                var result = await defaultClient.ProcessAsync(payload);
                if (result) return ProcessorType.Default;

                _defaultOk.Value = false;
                _ = Task.Delay(100).ContinueWith(_ => _defaultOk.Value = true);
            }
            catch
            {
                _defaultOk.Value = false;
                _ = Task.Delay(100).ContinueWith(_ => _defaultOk.Value = true);
            }
        }

        if (_fallbackOk.Value)
        {
            try
            {
                var fallbackClient = new PaymentProcessor(httpClientFactory.CreateClient("PaymentProcessorFallback"));
                var result = await fallbackClient.ProcessAsync(payload);
                if (result) return ProcessorType.Fallback;

                _fallbackOk.Value = false;
                _ = Task.Delay(100).ContinueWith(_ => _fallbackOk.Value = true);
            }
            catch
            {
                _fallbackOk.Value = false;
                _ = Task.Delay(100).ContinueWith(_ => _fallbackOk.Value = true);
            }
        }

        return null;
    }
}