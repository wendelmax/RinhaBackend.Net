namespace RinhaBackend.Net.Models.Requests;

public class PaymentRequest
{
    public Guid CorrelationId { get; set; }
    public decimal Amount { get; set; }
    
}