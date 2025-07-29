using Dapper;
using System.Data;
using Microsoft.Extensions.Logging;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Records;

namespace RinhaBackend.Net.Infrastructure.Repositories;

public sealed class SummaryRepository(IDbConnection connection, ILogger<SummaryRepository> logger)
{
    private const string SummaryQuery = """
                                            SELECT processor, SUM(amount) AS totalamount, COUNT(*) AS totalrequests
                                            FROM payments
                                            WHERE processor IS NOT NULL
                                            GROUP BY processor;
                                        """;

    public async Task<Summary> GetSummaryByRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing summary query from {From} to {To}", from, to);
            
            var rows = await connection.QueryAsync(SummaryQuery);

            logger.LogInformation("Query returned {RowCount} rows", rows.Count());

            var summary = new Summary();

            foreach (dynamic row in rows)
            {
                var processor = (int)row.processor;
                var totalAmount = (decimal)row.totalamount;
                var totalRequests = (long)row.totalrequests;

                var processorType = processor switch
                {
                    0 => ProcessorType.Default,
                    1 => ProcessorType.Fallback,
                    _ => (ProcessorType?)null
                };

                if (processorType is null)
                    continue;

                var detail = new SummaryDetail
                {
                    TotalAmount = totalAmount,
                    TotalRequests = totalRequests
                };

                summary.Processors[processorType.Value] = detail;
            }

            logger.LogInformation("Summary created successfully with {ProcessorCount} processors", summary.Processors.Count);
            return summary;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database query failed for range {From} to {To}", from, to);
            throw new InvalidOperationException($"Database query failed: {ex.Message}", ex);
        }
    }
}