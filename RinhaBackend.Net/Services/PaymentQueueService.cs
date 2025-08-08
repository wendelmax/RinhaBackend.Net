using System.Threading.Channels;
using RinhaBackend.Net.Models.Payloads;

namespace RinhaBackend.Net.Services;

public class PaymentQueueService : IPaymentQueueService
{
    private readonly Channel<PaymentPayload> _channel = Channel.CreateBounded<PaymentPayload>(new BoundedChannelOptions(20000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public void Enqueue(PaymentPayload payload)
    {
        if (!_channel.Writer.TryWrite(payload))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _channel.Writer.WriteAsync(payload);
                }
                catch (Exception)
                {
                    // Channel completed or cancelled
                }
            });
        }
    }

    public async Task<PaymentPayload?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_channel.Reader.TryRead(out var payload))
                {
                    return payload;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested
        }
        catch (ChannelClosedException)
        {
            // Channel completed
        }
        
        return null;
    }

    public Task<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
    }
}