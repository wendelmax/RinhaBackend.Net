using RinhaBackend.Net.Models.Payloads;
using System.Text.Json;
using RinhaBackend.Net.Serialization.Models;
using RinhaBackend.Net.Serialization;
using System.Net.Http.Json;


namespace RinhaBackend.Net.Infrastructure.Clients;

public class PaymentProcessor(HttpClient httpClient, ILogger<PaymentProcessor> logger) : IPaymentProcessorClient
{
    public async Task<bool> ProcessAsync(PaymentPayload payload, CancellationToken cancellationToken = default)
    {
        var requestPayload = new PaymentRequestDto
        {
            CorrelationId = payload.CorrelationId,
            Amount = payload.Amount,
            RequestedAt = payload.EffectiveRequestedAt
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(requestPayload, options: new JsonSerializerOptions
            {
                TypeInfoResolver = AppJsonSerializerContext.Default
            })
        };

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
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
