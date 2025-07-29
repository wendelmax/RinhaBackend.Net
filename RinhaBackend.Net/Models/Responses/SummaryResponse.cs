namespace RinhaBackend.Net.Models.Responses;

public class SummaryResponse
{
    public ProcessorSummary Default { get; set; } = new();
    public ProcessorSummary Fallback { get; set; } = new();
}