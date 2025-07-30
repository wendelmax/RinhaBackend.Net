using RinhaBackend.Net.Models.Payloads;
using System.Text.Json;


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

    public Task<HealthCheckResponse?> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<HealthCheckResponse>("/payments/service-health", cancellationToken);
    }
}

public class HealthCheckResponse
{
    public bool Failing { get; set; }
    public int MinResponseTime { get; set; }
}
