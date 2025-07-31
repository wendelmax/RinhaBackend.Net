using System.Globalization;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Responses;

namespace RinhaBackend.Net.Services;

public sealed class SummaryService(SummaryRepository summaryRepository)
{
    public Task<SummaryResponse> GetSummaryByRange(DateTimeOffset from, DateTimeOffset to)
    {
        var summary = summaryRepository.GetSummaryByRangeAsync(from, to).Result;

        if(summary.Processors.Count==0) return Task.FromResult(new SummaryResponse());
        
        return Task.FromResult(new SummaryResponse()
        {
            Default = summary.Processors[ProcessorType.Default],
            Fallback = summary.Processors[ProcessorType.Fallback],
        });
    }
}