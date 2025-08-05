using Dapper;
using System.Data;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Records;
using RinhaBackend.Net.Models.Responses;

namespace RinhaBackend.Net.Infrastructure.Repositories;

public sealed class SummaryRepository(IDbConnection connection, ILogger<SummaryRepository> logger)
{
    private const string SummaryQueryWithRange = """
                                        SELECT processor, SUM(amount) AS totalamount, COUNT(*) AS totalrequests
                                        FROM payments
                                        WHERE processor IS NOT NULL
                                        AND requested_at >= @From
                                        AND requested_at <= @To
                                        GROUP BY processor;
                                    """;

    private const string SummaryQueryAll = """
                                        SELECT processor, SUM(amount) AS totalamount, COUNT(*) AS totalrequests
                                        FROM payments
                                        WHERE processor IS NOT NULL
                                        GROUP BY processor;
                                    """;

    public async Task<Summary> GetSummaryByRangeAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing summary query from {From} to {To}", from, to);

            IEnumerable<dynamic> rows;
            
            if (from.HasValue && to.HasValue)
            {
                var parameters = new DynamicParameters();
                parameters.Add("@From", from.Value.UtcDateTime, DbType.DateTime);
                parameters.Add("@To", to.Value.UtcDateTime, DbType.DateTime);
                rows = await connection.QueryAsync(SummaryQueryWithRange, parameters, commandTimeout: 30);
            }
            else
            {
                rows = await connection.QueryAsync(SummaryQueryAll, commandTimeout: 30);
            }

            var enumerable = rows as dynamic[] ?? rows.ToArray();
            logger.LogInformation("Query returned {RowCount} rows", enumerable.Length);

            var summary = new Summary();

            foreach (var row in enumerable)
            {
                var processorValue = Convert.ToInt32(row.processor);
                var processor = processorValue == 0 ? ProcessorType.Default : ProcessorType.Fallback;
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