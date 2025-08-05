using Dapper;
using System.Data;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Models.Records;

namespace RinhaBackend.Net.Infrastructure.Repositories;

public sealed class PaymentRepository(IDbConnection connection)
{
    private readonly ILogger<PaymentRepository> _logger = new LoggerFactory().CreateLogger<PaymentRepository>();
    
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
        
        _logger.LogInformation("Attempting to insert payment {CorrelationId} with processor {Processor} and requestedAt {RequestedAt} (UTC: {UtcDateTime})", 
            payment.CorrelationId, processor, payment.EffectiveRequestedAt, utcDateTime);
        
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                _logger.LogInformation("Opening database connection for payment {CorrelationId}", payment.CorrelationId);
                connection.Open();
            }
            
            var affectedRows = await connection.ExecuteAsync(insertQuery, parameters);
            _logger.LogInformation("SQL execution completed for {CorrelationId}, affected rows: {AffectedRows}", 
                payment.CorrelationId, affectedRows);
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("Payment inserted successfully: {CorrelationId} with {Processor}", 
                    payment.CorrelationId, processor);
                return true;
            }
            else
            {
                _logger.LogWarning("Payment already exists, skipping: {CorrelationId}", payment.CorrelationId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting payment {CorrelationId}", payment.CorrelationId);
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
            _logger.LogError(ex, "Error querying payment {CorrelationId}", correlationId);
            throw;
        }
    }

}