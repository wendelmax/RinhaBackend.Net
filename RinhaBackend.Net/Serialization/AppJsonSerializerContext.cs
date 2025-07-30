using System.Text.Json.Serialization;
using RinhaBackend.Net.Models.Entities;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Models.Requests;
using RinhaBackend.Net.Models.Responses;
using RinhaBackend.Net.Models.Records;
using RinhaBackend.Net.Models.Enums;

namespace RinhaBackend.Net.Serialization;

[JsonSerializable(typeof(SummaryRequest))]
[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentPayload))]
[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(SummaryResponse))]
[JsonSerializable(typeof(Summary))]
[JsonSerializable(typeof(ProcessorType))]
[JsonSerializable(typeof(ProcessorSummary))]
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}