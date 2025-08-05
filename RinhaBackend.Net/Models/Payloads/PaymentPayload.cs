using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace RinhaBackend.Net.Models.Payloads;

public sealed class PaymentPayload
{
    [JsonPropertyName("correlationId")]
    public Guid CorrelationId { get; init; }
    
    [JsonPropertyName("amount")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero")]
    public decimal Amount { get; init; }
    
    [JsonPropertyName("requestedAt")]
    public DateTimeOffset? RequestedAt { get; init; }
    
    [JsonIgnore]
    public DateTimeOffset EffectiveRequestedAt => RequestedAt ?? DateTimeOffset.UtcNow;
    
    [JsonIgnore]
    public int RetryCount { get; init; } = 0;
    
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
    
    public bool IsValidRequestedAt => true; // Always valid since we use EffectiveRequestedAt
    
    public bool IsValid()
    {
        return CorrelationId != Guid.Empty && 
               Amount > 0 && 
               IsValidRequestedAt;
    }
    
    public IEnumerable<string> GetValidationErrors()
    {
        if (CorrelationId == Guid.Empty)
            yield return "CorrelationId cannot be empty";
        
        if (Amount <= 0)
            yield return "Amount must be greater than zero";
    }
}