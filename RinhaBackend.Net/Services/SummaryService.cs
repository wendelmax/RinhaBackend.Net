using System.Globalization;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Responses;

namespace RinhaBackend.Net.Services;

public sealed class SummaryService(SummaryRepository summaryRepository)
{
    public async Task<SummaryResponse> GetSummaryByRange(DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var summary = await summaryRepository.GetSummaryByRangeAsync(from, to);

        if (summary.Processors.Count == 0)
        {
            return new SummaryResponse();
        }

        var result = new SummaryResponse();

        if (summary.Processors.TryGetValue(ProcessorType.Default, out var defaultProcessor))
        {
            result.Default = defaultProcessor;
        }

        if (summary.Processors.TryGetValue(ProcessorType.Fallback, out var fallbackProcessor))
        {
            result.Fallback = fallbackProcessor;
        }

        return result;
    }
}