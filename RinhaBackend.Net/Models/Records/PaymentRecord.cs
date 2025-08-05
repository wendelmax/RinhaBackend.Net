using System.Text.Json.Serialization;
using RinhaBackend.Net.Models.Enums;

namespace RinhaBackend.Net.Models.Records;

public sealed record PaymentRecord
{
    [JsonPropertyName("correlationId")]
    public Guid CorrelationId { get; init; }
    
    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }
    
    [JsonPropertyName("processor")]
    public ProcessorType Processor { get; init; }
    
    [JsonPropertyName("requestedAt")]
    public DateTimeOffset RequestedAt { get; init; }
} 