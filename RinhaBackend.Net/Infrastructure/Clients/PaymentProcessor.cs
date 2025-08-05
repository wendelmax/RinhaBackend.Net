using RinhaBackend.Net.Models.Payloads;
using System.Text.Json;


namespace RinhaBackend.Net.Infrastructure.Clients;

public class PaymentProcessor(HttpClient httpClient) : IPaymentProcessorClient
{
    ILogger<PaymentProcessor> logger = new LoggerFactory().CreateLogger<PaymentProcessor>();
    public async Task<bool> ProcessAsync(PaymentPayload payload, CancellationToken cancellationToken = default)
    {
        // Create a new payload with EffectiveRequestedAt instead of nullable RequestedAt
        var requestPayload = new
        {
            correlationId = payload.CorrelationId,
            amount = payload.Amount,
            requestedAt = payload.EffectiveRequestedAt
        };
        
        var response = await httpClient.PostAsJsonAsync("/payments", requestPayload, cancellationToken);
        logger.LogInformation("Payment processor response: {StatusCode} for {CorrelationId}", 
            response.StatusCode, payload.CorrelationId);
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
