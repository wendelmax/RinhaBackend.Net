using RinhaBackend.Net.Models.Payloads;
using System.Text.Json;


namespace RinhaBackend.Net.Infrastructure.Clients;

public class PaymentProcessor(HttpClient httpClient) : IPaymentProcessorClient
{
    ILogger<PaymentProcessor> logger = new LoggerFactory().CreateLogger<PaymentProcessor>();
    public async Task<bool> ProcessAsync(PaymentPayload payload, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/payments", payload, cancellationToken);
        logger.LogInformation(response.ToString());
        return response.IsSuccessStatusCode;
    }

    public Task<HealthCheckResponse?> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return httpClient.GetFromJsonAsync<HealthCheckResponse>("/payments/service-health", cancellationToken);
    }
}

public class HealthCheckResponse
{
    public bool Failing { get; set; }
    public int MinResponseTime { get; set; }
}
