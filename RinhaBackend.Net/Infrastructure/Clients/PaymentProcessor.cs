using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Models.Responses;

namespace RinhaBackend.Net.Infrastructure.Clients;

public class PaymentProcessor : IPaymentProcessorClient
{
    private readonly HttpClient _httpClient;

    public PaymentProcessor(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> ProcessAsync(PaymentPayload payload, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/payments", payload, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/payments/service-health", cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
