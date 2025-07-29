using RinhaBackend.Net.Infrastructure.Clients;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Models.Enums;
using RinhaBackend.Net.Services;
using System.Data;
using Dapper;
using Npgsql;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Serialization;
using RinhaBackend.Net.Models.Responses;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgresConnection");
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Creating database connection with connection string: {ConnectionString}", connStr);
    return new NpgsqlConnection(connStr);
});

builder.Services.AddScoped<PaymentRepository>();
builder.Services.AddScoped<SummaryRepository>();
builder.Services.AddScoped<ProcessorService>();
builder.Services.AddScoped<SummaryService>();

builder.Services.AddHttpClient<IPaymentProcessorClient, PaymentProcessor>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/db-test", (IDbConnection connection, ILogger<Program> logger) =>
{
    try
    {
        connection.Open();
        var result = connection.QuerySingle<int>("SELECT 1");
        connection.Close();
        return Results.Ok(new { status = "database_connected", result });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database connection test failed");
        return Results.Problem($"Database connection failed: {ex.Message}", statusCode: 500);
    }
});

app.MapGet("/payments-summary", async (string from, string to, SummaryService summaryService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("Processing summary request from {From} to {To}", from, to);
        var result = await summaryService.GetSummaryByRange(from, to);
        logger.LogInformation("Summary request completed successfully");
        
        var response = new SummaryResponse();
        
        if (result.Processors.TryGetValue(ProcessorType.Default, out var defaultDetail))
        {
            response.Default.TotalAmount = defaultDetail.TotalAmount;
            response.Default.TotalRequests = defaultDetail.TotalRequests;
        }
        
        if (result.Processors.TryGetValue(ProcessorType.Fallback, out var fallbackDetail))
        {
            response.Fallback.TotalAmount = fallbackDetail.TotalAmount;
            response.Fallback.TotalRequests = fallbackDetail.TotalRequests;
        }
        
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing summary request from {From} to {To}", from, to);
        return Results.Problem($"Error retrieving summary: {ex.Message}", statusCode: 500);
    }
});

app.MapPost("/payments", (PaymentPayload request, ProcessorService processorService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("Processing payment request with correlation ID: {CorrelationId}", request.CorrelationId);
        processorService.Enqueue(request);
        logger.LogInformation("Payment request completed successfully");
        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing payment request with correlation ID: {CorrelationId}", request.CorrelationId);
        return Results.Problem($"Error processing payment: {ex.Message}", statusCode: 500);
    }
});

app.Run();
