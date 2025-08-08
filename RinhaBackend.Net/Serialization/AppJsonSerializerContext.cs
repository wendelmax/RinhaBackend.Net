using System.Text.Json.Serialization;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Models.Responses;
using RinhaBackend.Net.Models.Records;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Serialization.Models;

namespace RinhaBackend.Net.Serialization;

[JsonSerializable(typeof(PaymentPayload))]
[JsonSerializable(typeof(SummaryResponse))]
[JsonSerializable(typeof(Summary))]
[JsonSerializable(typeof(ProcessorType))]
[JsonSerializable(typeof(ProcessorSummary))]
[JsonSerializable(typeof(PaymentRequestDto))]
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}