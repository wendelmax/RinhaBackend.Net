using RinhaBackend.Net.Infrastructure.Clients;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Services;
using System.Data;
using Dapper;
using Npgsql;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Serialization;


var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgresConnection");
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Creating database connection factory");
    
    return new NpgsqlConnection(connStr);
});

builder.Services.AddScoped<PaymentRepository>();
builder.Services.AddScoped<SummaryRepository>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddSingleton<IPaymentQueueService, PaymentQueueService>();
builder.Services.AddHostedService<PaymentProcessorWorker>();

builder.Services.AddHttpClient("PaymentProcessorDefault", client =>
{
    var baseUrl = builder.Configuration["PaymentProcessorDefault:BaseUrl"] ?? "http://payment-processor-default:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient("PaymentProcessorFallback", client =>
{
    var baseUrl = builder.Configuration["PaymentProcessorFallback:BaseUrl"] ?? "http://payment-processor-fallback:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<IPaymentProcessorClient, PaymentProcessor>();

var app = builder.Build();

// Add rate limiting
app.Use(async (context, next) =>
{
    // Add performance headers
    context.Response.Headers["X-Response-Time"] = DateTimeOffset.UtcNow.ToString("O");
    context.Response.Headers["X-Powered-By"] = "RinhaBackend.NET";
    
    await next();
});

app.MapGet("/health", () => "healthy");

app.MapGet("/metrics", (IServiceProvider serviceProvider) =>
{
    var memoryInfo = GC.GetGCMemoryInfo();
    
    return Results.Ok(new
    {
        timestamp = DateTimeOffset.UtcNow,
        uptime = Environment.TickCount64,
        memory = new
        {
            totalAllocated = GC.GetTotalMemory(false),
            heapSize = memoryInfo.HeapSizeBytes,
            totalMemory = GC.GetTotalMemory(true)
        }
    });
});

app.MapGet("/payments-summary", async (DateTimeOffset? from, DateTimeOffset? to, IServiceProvider serviceProvider, ILogger<Program> logger) =>
{
    try
    {
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
        {
            logger.LogWarning("Invalid date range: from {From} to {To}", from, to);
            return Results.BadRequest(new { error = "From date must be before To date" });
        }
        
        using var scope = serviceProvider.CreateScope();
        var summaryService = scope.ServiceProvider.GetRequiredService<SummaryService>();
        
        logger.LogInformation("Summary request received from {From} to {To}", from, to);
        
        var result = await summaryService.GetSummaryByRange(from, to);
        
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing summary request from {From} to {To}", from, to);
        return Results.StatusCode(500);
    }
});

app.MapPost("/payments-test-datetime", async (PaymentPayload request, IServiceProvider serviceProvider, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("Testing DateTime conversion for payment {CorrelationId} with requestedAt {RequestedAt}", 
            request.CorrelationId, request.RequestedAt);
        
        return Results.Ok(new { 
            success = true, 
            correlationId = request.CorrelationId,
            requestedAt = request.RequestedAt,
            effectiveRequestedAt = request.EffectiveRequestedAt,
            requestedAtUtc = request.EffectiveRequestedAt.UtcDateTime,
            isValid = request.IsValidRequestedAt,
            message = "DateTime test completed - payment will be processed by Worker"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error testing DateTime conversion for {CorrelationId}", request.CorrelationId);
        return Results.StatusCode(500);
    }
});

app.MapPost("/payments", (PaymentPayload request, IPaymentQueueService queueService, ILogger<Program> logger) =>
{
    try
    {
        if (!request.IsValid())
        {
            var errors = request.GetValidationErrors().ToList();
            logger.LogWarning("Invalid payment request: {CorrelationId}, Errors: {Errors}", request.CorrelationId, string.Join(", ", errors));
            return Results.BadRequest(new { errors });
        }
        
        queueService.Enqueue(request);
        logger.LogInformation("Payment enqueued for processing: {CorrelationId} with amount {Amount}", request.CorrelationId, request.Amount);
        return Results.Accepted();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing payment request with correlation ID: {CorrelationId}", request.CorrelationId);
        return Results.StatusCode(500);
    }
});

app.MapPost("/purge-payments", async (IServiceProvider serviceProvider, ILogger<Program> logger) =>
{
    try
    {
        using var scope = serviceProvider.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var summaryService = scope.ServiceProvider.GetRequiredService<SummaryService>();
        
        logger.LogInformation("Purge payments request received");
        
        const string purgeQuery = "DELETE FROM payments";
        
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
        
        var affectedRows = await connection.ExecuteAsync(purgeQuery);
        
        logger.LogInformation("Purged {AffectedRows} payment records", affectedRows);
        
        return Results.Ok(new { 
            message = $"Successfully purged {affectedRows} payment records", 
            affectedRows,
            timestamp = DateTimeOffset.UtcNow
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error purging payments");
        return Results.StatusCode(500);
    }
});

app.Run(); 