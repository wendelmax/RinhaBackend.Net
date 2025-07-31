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
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("PaymentProcessorFallback", client =>
{
    var baseUrl = builder.Configuration["PaymentProcessorFallback:BaseUrl"] ?? "http://payment-processor-fallback:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<IPaymentProcessorClient, PaymentProcessor>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/payments-summary", async (DateTimeOffset from, DateTimeOffset to, IServiceProvider serviceProvider, ILogger<Program> logger) =>
{
    try
    {
        using var scope = serviceProvider.CreateScope();
        var summaryService = scope.ServiceProvider.GetRequiredService<SummaryService>();
        var result = await summaryService.GetSummaryByRange(from, to);
        
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing summary request from {From} to {To}", from, to);
        return Results.Problem($"Error retrieving summary: {ex.Message}", statusCode: 500);
    }
});

app.MapPost("/payments-test-datetime", async (PaymentPayload request, IServiceProvider serviceProvider, ILogger<Program> logger) =>
{
    try
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<PaymentRepository>();
        
        logger.LogInformation("Testing DateTime conversion for payment {CorrelationId} with requestedAt {RequestedAt}", 
            request.CorrelationId, request.RequestedAt);
        
        var result = await repository.InsertAsync(request, ProcessorType.Default);
        
        return Results.Ok(new { 
            success = result, 
            correlationId = request.CorrelationId,
            requestedAt = request.RequestedAt,
            requestedAtUtc = request.RequestedAt.UtcDateTime,
            isValid = request.IsValidRequestedAt
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error testing DateTime conversion for {CorrelationId}", request.CorrelationId);
        return Results.Problem($"Error testing DateTime conversion: {ex.Message}", statusCode: 500);
    }
});

app.MapPost("/payments", (PaymentPayload request, IPaymentQueueService queueService, ILogger<Program> logger) =>
{
    try
    {
        queueService.Enqueue(request);
        logger.LogInformation("Payment enqueued for processing: {CorrelationId} e amount {Amount}", request.CorrelationId, request.Amount);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing payment request with correlation ID: {CorrelationId}", request.CorrelationId);
        return Results.Problem($"Error processing payment: {ex.Message}", statusCode: 500);
    }
});


app.Run(); 