using System.Text.Json.Serialization;

namespace RinhaBackend.Net.Models.Responses;

public class PaymentResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}