using System.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using Npgsql;
using RinhaBackend.Net.Infrastructure.Repositories;
using RinhaBackend.Net.Models.Payloads;
using RinhaBackend.Net.Models.Requests;
using RinhaBackend.Net.Serialization;
using RinhaBackend.Net.Services;

var builder = WebApplication.CreateSlimBuilder(args);
var configuration = builder.Configuration;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddHttpClient("PaymentProcessorDefault", client =>
{
    client.BaseAddress = new Uri(configuration["PaymentProcessorDefault:BaseUrl"] ?? string.Empty);
});

builder.Services.AddHttpClient("PaymentProcessorFallback", client =>
{
    client.BaseAddress = new Uri(configuration["PaymentProcessorFallback:BaseUrl"] ?? string.Empty);
});

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgresConnection");
    return new NpgsqlConnection(connStr);
});

builder.Services.AddScoped<PaymentRepository>();
builder.Services.AddScoped<SummaryRepository>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<ProcessorService>();

var app = builder.Build();

app.MapGet("/payments-summary", async (string from, string to, SummaryService summaryService) =>
{
    var result = await summaryService.GetSummaryByRange(from, to);
    return Results.Ok(result);
});

app.MapPost("/payments", (PaymentRequest payment, ProcessorService processorService) =>
{
    var dto = new PaymentPayload
    {
        CorrelationId = payment.CorrelationId,
        Amount = payment.Amount,
        RequestedAt = DateTime.UtcNow
    };

    processorService.Enqueue(dto);
    
    return Results.Ok();
});

app.Run();
