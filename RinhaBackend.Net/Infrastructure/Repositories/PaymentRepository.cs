using Dapper;
using System.Data;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Models.Records;

namespace RinhaBackend.Net.Infrastructure.Repositories;

public sealed class PaymentRepository(IDbConnection connection, ILogger<PaymentRepository> logger)
{
    
    public async Task<bool> InsertAsync(PaymentPayload payment, ProcessorType processor)
    {
        const string insertQuery = """
            INSERT INTO payments (correlation_id, amount, processor, requested_at)
            VALUES (@CorrelationId, @Amount, @Processor, @RequestedAt)
            ON CONFLICT (correlation_id) DO NOTHING;
        """;

        var utcDateTime = payment.EffectiveRequestedAt.UtcDateTime;
        
        var parameters = new DynamicParameters();
        parameters.Add("@CorrelationId", payment.CorrelationId);
        parameters.Add("@Amount", payment.Amount);
        parameters.Add("@Processor", processor);
        parameters.Add("@RequestedAt", utcDateTime);
        
        
        
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }
            
            var affectedRows = await connection.ExecuteAsync(insertQuery, parameters);
            
            if (affectedRows > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }
    
    public async Task<PaymentRecord?> GetByCorrelationIdAsync(Guid correlationId)
    {
        const string selectQuery = """
            SELECT correlation_id, amount, processor, requested_at
            FROM payments
            WHERE correlation_id = @CorrelationId;
        """;
        
        var parameters = new DynamicParameters();
        parameters.Add("@CorrelationId", correlationId);
        
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }
            
            var result = await connection.QueryFirstOrDefaultAsync<PaymentRecord>(selectQuery, parameters);
            return result;
        }
        catch (Exception ex)
        {
            throw;
        }
    }

}