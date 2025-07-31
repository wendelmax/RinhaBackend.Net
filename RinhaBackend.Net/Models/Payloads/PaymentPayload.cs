using System.Text.Json.Serialization;

namespace RinhaBackend.Net.Models.Payloads;

public sealed class PaymentPayload
{
    [JsonPropertyName("correlationId")]
    public Guid CorrelationId { get; init; }
    
    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }
    
    [JsonPropertyName("requestedAt")]
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
    
    [JsonIgnore]
    public int RetryCount { get; set; } = 0;
    
    public const int MaxRetries = 5;
    
    public bool ShouldRetry => RetryCount < MaxRetries;
    
    public PaymentPayload IncrementRetry()
    {
        return new PaymentPayload
        {
            CorrelationId = this.CorrelationId,
            Amount = this.Amount,
            RequestedAt = this.RequestedAt,
            RetryCount = this.RetryCount + 1
        };
    }
    
    public bool IsValidRequestedAt => RequestedAt != default && RequestedAt != DateTimeOffset.MinValue;
}