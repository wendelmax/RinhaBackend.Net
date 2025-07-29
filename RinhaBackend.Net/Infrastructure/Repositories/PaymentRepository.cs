using Dapper;
using System.Data;
using RinhaBackend.Net.Models.Entities;

namespace RinhaBackend.Net.Infrastructure.Repositories;

public sealed class PaymentRepository(IDbConnection connection)
{
    public async Task InsertAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        const string insertQuery = """
                                       INSERT INTO payments (correlation_id, amount, requested_at, status, processor)
                                       VALUES (@CorrelationId, @Amount, @RequestedAt, @Status, @Processor);
                                   """;

        await connection.ExecuteAsync(insertQuery, payment);
    }
}