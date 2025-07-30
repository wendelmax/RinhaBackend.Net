namespace RinhaBackend.Net.Models.Payloads;

public sealed class PaymentPayload
{
    public Guid CorrelationId { get; set; }
    public decimal Amount { get; set; }
}