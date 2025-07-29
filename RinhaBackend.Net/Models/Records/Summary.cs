using System.Text.Json.Serialization;
using RinhaBackend.Net.Models.Enums;

namespace RinhaBackend.Net.Models.Records;

public sealed class Summary
{
    public Dictionary<ProcessorType, SummaryDetail> Processors { get; set; } = new();
}
