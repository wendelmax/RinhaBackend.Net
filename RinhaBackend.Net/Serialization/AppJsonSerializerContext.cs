using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RinhaBackend.Net.Models.Entities;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Models.Requests;
using RinhaBackend.Net.Models.Responses;

namespace RinhaBackend.Net.Serialization;

[JsonSerializable(typeof(SummaryRequest))]
[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentPayload))]
[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(SummaryResponse))]
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}