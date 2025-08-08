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
                                        AND requested_at < @To
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
            IEnumerable<(int processor, decimal totalamount, long totalrequests)> rows;
            
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            if (from.HasValue && to.HasValue)
            {
                var parameters = new DynamicParameters();
                parameters.Add("@From", from.Value, DbType.DateTimeOffset);
                parameters.Add("@To", to.Value, DbType.DateTimeOffset);
                rows = await connection.QueryAsync<(int processor, decimal totalamount, long totalrequests)>(SummaryQueryWithRange, parameters, commandTimeout: 15);
            }
            else
            {
                rows = await connection.QueryAsync<(int processor, decimal totalamount, long totalrequests)>(SummaryQueryAll, commandTimeout: 15);
            }

            var enumerable = rows as (int processor, decimal totalamount, long totalrequests)[] ?? rows.ToArray();

            var summary = new Summary();

            foreach (var row in enumerable)
            {
                var processor = row.processor == 0 ? ProcessorType.Default : ProcessorType.Fallback;
                var totalAmount = row.totalamount;
                var totalRequests = row.totalrequests;

                var detail = new ProcessorSummary
                {
                    TotalAmount = totalAmount,
                    TotalRequests = totalRequests
                };

                summary.Processors[processor] = detail;
            }

            return summary;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Database query failed: {ex.Message}", ex);
        }
    }
} 