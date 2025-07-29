namespace RinhaBackend.Net.Models.Payloads;

public sealed class PaymentPayload
{
    public Guid CorrelationId { get; set; }
    public decimal Amount { get; set; }
    public DateTime RequestedAt { get; set; }

    public override string ToString()
    {
        return $"PaymentProcessorPayload {{ CorrelationId = {CorrelationId}, Amount = {Amount}, RequestedAt = {RequestedAt} }}";
    }
}