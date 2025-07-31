using Dapper;
using System.Data;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Records;
using RinhaBackend.Net.Models.Responses;

namespace RinhaBackend.Net.Infrastructure.Repositories;

public sealed class SummaryRepository(IDbConnection connection, ILogger<SummaryRepository> logger)
{
    private const string SummaryQuery = """
                                        SELECT processor, SUM(amount) AS totalamount, COUNT(*) AS totalrequests
                                        FROM payments
                                        WHERE processor IS NOT NULL
                                        AND requested_at BETWEEN @From AND @To
                                        GROUP BY processor;
                                    """;

    public async Task<Summary> GetSummaryByRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing summary query from {From} to {To}", from, to);

            var parameters = new DynamicParameters();
            parameters.Add("@From", from);
            parameters.Add("@To", to);
            var rows = await connection.QueryAsync(SummaryQuery, parameters, commandTimeout: 30);

            var enumerable = rows as dynamic[] ?? rows.ToArray();
            logger.LogInformation("Query returned {RowCount} rows", enumerable.Length);

            var summary = new Summary();

            foreach (var row in enumerable)
            {
                var processor = (ProcessorType)row.processor;
                var totalAmount = (decimal)row.totalamount;
                var totalRequests = (long)row.totalrequests;

                logger.LogInformation("ProcessorXPTO {Processor}", processor);

                var detail = new ProcessorSummary
                {
                    TotalAmount = totalAmount,
                    TotalRequests = totalRequests
                };

                summary.Processors[processor] = detail;
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