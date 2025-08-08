using RinhaBackend.Net.Infrastructure.Clients;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Services;
using System.Data;
using Dapper;
using Npgsql;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Serialization;
using Microsoft.Extensions.Logging;


var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.None);

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgresConnection");
    
    var builder = new NpgsqlConnectionStringBuilder(connStr)
    {
        Pooling = true
    };
    return new NpgsqlConnection(builder.ConnectionString);
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
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
    MaxConnectionsPerServer = 256,
    EnableMultipleHttp2Connections = true,
    AutomaticDecompression = System.Net.DecompressionMethods.None
});

builder.Services.AddHttpClient("PaymentProcessorFallback", client =>
{
    var baseUrl = builder.Configuration["PaymentProcessorFallback:BaseUrl"] ?? "http://payment-processor-fallback:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
    MaxConnectionsPerServer = 256,
    EnableMultipleHttp2Connections = true,
    AutomaticDecompression = System.Net.DecompressionMethods.None
});

builder.Services.AddHttpClient<IPaymentProcessorClient, PaymentProcessor>();

var app = builder.Build();

// Prefer Kestrel with higher request queue and HTTP/1.1 keep-alive tuning
app.Lifetime.ApplicationStarted.Register(() =>
{
});

// Removed per-request header middleware to avoid overhead

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
            totalMemory = memoryInfo.TotalCommittedBytes
        }
    });
});

app.MapGet("/payments-summary", async (DateTimeOffset? from, DateTimeOffset? to, IServiceProvider serviceProvider) =>
{
    try
    {
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
        {
            return Results.BadRequest(new { error = "From date must be before To date" });
        }
        
        using var scope = serviceProvider.CreateScope();
        var summaryService = scope.ServiceProvider.GetRequiredService<SummaryService>();
        var result = await summaryService.GetSummaryByRange(from, to);
        
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.StatusCode(500);
    }
});

app.MapPost("/payments-test-datetime", async (PaymentPayload request, IServiceProvider serviceProvider) =>
{
    try
    {
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
        return Results.StatusCode(500);
    }
});

app.MapPost("/payments", (PaymentPayload request, IPaymentQueueService queueService) =>
{
    try
    {
        if (!request.IsValid())
        {
            var errors = request.GetValidationErrors().ToList();
            return Results.BadRequest(new { errors });
        }

        var payload = request.RequestedAt.HasValue
            ? request
            : new PaymentPayload
            {
                CorrelationId = request.CorrelationId,
                Amount = request.Amount,
                RequestedAt = DateTimeOffset.UtcNow,
                RetryCount = request.RetryCount
            };

        queueService.Enqueue(payload);
        return Results.Accepted();
    }
    catch (Exception ex)
    {
        return Results.StatusCode(500);
    }
});

app.MapPost("/purge-payments", async (IServiceProvider serviceProvider) =>
{
    try
    {
        using var scope = serviceProvider.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var summaryService = scope.ServiceProvider.GetRequiredService<SummaryService>();
        const string purgeQuery = "DELETE FROM payments";
        
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
        
        var affectedRows = await connection.ExecuteAsync(purgeQuery);
        return Results.Ok(new { 
            message = $"Successfully purged {affectedRows} payment records", 
            affectedRows,
            timestamp = DateTimeOffset.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.StatusCode(500);
    }
});

app.Run(); 