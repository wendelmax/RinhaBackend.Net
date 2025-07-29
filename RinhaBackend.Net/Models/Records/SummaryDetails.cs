using System.Text.Json.Serialization;

namespace RinhaBackend.Net.Models.Records;

public sealed record SummaryDetail(
    [property: JsonPropertyName("totalRequests")] long TotalRequests = 0,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount = 0m
)
{
    public SummaryDetail Increase(decimal amount) =>
        new SummaryDetail(TotalAmount: TotalAmount + amount, TotalRequests: TotalRequests + 1);
}