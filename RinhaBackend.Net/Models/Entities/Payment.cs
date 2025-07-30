using System.Text;
using RinhaBackend.Net.Models.Enums;

namespace RinhaBackend.Net.Models.Entities;

public sealed class Payment
{
    public Guid CorrelationId { get; set; }
    public decimal Amount { get; set; }
    public ProcessorType Processor { get; set; }
}