using System.Text.Json.Serialization;

namespace RinhaBackend.Net.Serialization.Models;

public sealed class PaymentRequestDto
{
    [JsonPropertyName("correlationId")]
    public Guid CorrelationId { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("requestedAt")]
    public DateTimeOffset RequestedAt { get; init; }
}


