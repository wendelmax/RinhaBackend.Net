using System.Globalization;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Models.Records;

namespace RinhaBackend.Net.Services;

public sealed class SummaryService(SummaryRepository summaryRepository)
{
    private static readonly string[] DateFormats = 
    {
        "yyyy-MM-dd'T'HH:mm:ss.fffK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd"
    };
    
    private static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;

    public Task<Summary> GetSummaryByRange(string? from, string? to)
    {
        var dateRange = ParseDateRange(from, to);
        return summaryRepository.GetSummaryByRangeAsync(dateRange.From, dateRange.To);
    }

    private static (DateTime From, DateTime To) ParseDateRange(string? from, string? to)
    {
        try
        {
            DateTime fromDate = TryParseOrDefault(from, DateTime.UnixEpoch);
            DateTime toDate = TryParseOrDefault(to, DateTime.UtcNow);

            return (fromDate, toDate);
        }
        catch (FormatException ex)
        {
            throw new BadHttpRequestException($"Invalid date format: {ex.Message}");
        }
    }

    private static DateTime TryParseOrDefault(string? dateText, DateTime defaultValue)
    {
        if (string.IsNullOrWhiteSpace(dateText))
            return defaultValue;

        if (DateTime.TryParseExact(dateText, DateFormats, DateCulture, DateTimeStyles.AdjustToUniversal, out var result))
            return result;

        throw new FormatException($"String '{dateText}' was not recognized as a valid DateTime.");
    }
}