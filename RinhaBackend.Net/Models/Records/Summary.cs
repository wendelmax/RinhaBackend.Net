using System.Text.Json.Serialization;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Responses;

namespace RinhaBackend.Net.Models.Records;

public sealed class Summary
{
    public Dictionary<ProcessorType, ProcessorSummary> Processors { get; set; } = new();
}
