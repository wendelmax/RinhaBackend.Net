using Dapper;
using System.Data;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Records;

namespace RinhaBackend.Net.Infrastructure.Repositories;

public sealed class SummaryRepository(IDbConnection connection)
{
    private const string SummaryQuery = """
                                            SELECT processor, SUM(amount) AS totalAmount, COUNT(*) AS totalRequests
                                            FROM payments
                                            WHERE processor IS NOT NULL
                                            AND requested_at BETWEEN @From AND @To
                                            GROUP BY processor;
                                        """;

    public async Task<Summary> GetSummaryByRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var rows = await connection.QueryAsync<SummaryRow>(
            SummaryQuery,
            new { From = from, To = to }
        );

        var summary = new Summary();

        foreach (var row in rows)
        {
            var processorType = row.Processor switch
            {
                0 => ProcessorType.Default,
                1 => ProcessorType.Fallback,
                _ => (ProcessorType?)null
            };

            if (processorType is null)
                continue;

            var detail = new SummaryDetail
            {
                TotalAmount = row.TotalAmount,
                TotalRequests = row.TotalRequests
            };

            summary.Processors[processorType.Value] = detail;
        }

        return summary;
    }

    private sealed record SummaryRow(int Processor, decimal TotalAmount, long TotalRequests);
}