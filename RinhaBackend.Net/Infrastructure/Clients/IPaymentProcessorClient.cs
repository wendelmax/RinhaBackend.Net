using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Models.Responses;

namespace RinhaBackend.Net.Infrastructure.Clients;

public interface IPaymentProcessorClient
{
    Task<bool> ProcessAsync(PaymentPayload payload, CancellationToken cancellationToken = default);
    Task<HealthCheckResponse?> HealthCheckAsync(CancellationToken cancellationToken = default);
}
