using RinhaBackend.Net.Models.Payloads;

namespace RinhaBackend.Net.Services;

public interface IPaymentQueueService
{
    void Enqueue(PaymentPayload payload);
    Task<PaymentPayload?> DequeueAsync(CancellationToken cancellationToken = default);
    Task<bool> WaitToReadAsync(CancellationToken cancellationToken = default);
}