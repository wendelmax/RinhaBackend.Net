using Dapper;
using System.Data;
using RinhaBackend.Net.Models.Entities;

namespace RinhaBackend.Net.Infrastructure.Repositories;

public sealed class PaymentRepository(IDbConnection connection)
{
    public async Task InsertAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        const string insertQuery = """
                                       INSERT INTO payments (correlationId, amount, processor, createdAt)
                                       VALUES (@CorrelationId, @Amount, @Processor, CURRENT_TIMESTAMP);
                                   """;

        await connection.ExecuteAsync(insertQuery, payment);
    }
}