using ChargesApi.Domain;
using ChargesApi.Services;
using ChargesApi.Stores;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ChargeStore>();
builder.Services.AddTransient<PaymentProcessor>();
builder.Services.AddTransient<AuditLog>();
var app = builder.Build();

app.MapPost("/charges", async (ChargeRequest chargeRequest, ChargeStore chargeStore, PaymentProcessor paymentProcessor, AuditLog auditLog) =>
{
    if (string.IsNullOrWhiteSpace(chargeRequest.IdempotencyKey))
        return Results.BadRequest(new { error = "idempotencyKey is required" });

    var processedCharge = await chargeStore.GetOrCreateAsync(chargeRequest.IdempotencyKey, async () =>
    {
        var createdCharge = await paymentProcessor.ChargeAsync(chargeRequest);
        _ = auditLog.LogChargeAsync(createdCharge, chargeRequest.CustomerEmail);
        return createdCharge;
    });

    return Results.Created($"/charges/{processedCharge.Id}", processedCharge);
});

app.MapGet("/charges/{chargeId}", (string chargeId, ChargeStore chargeStore) =>
{
    var storedCharge = chargeStore.GetById(chargeId);
    return storedCharge is null ? Results.NotFound() : Results.Ok(storedCharge);
});

app.MapGet("/customers/search", ([FromQuery(Name = "email")] string customerEmail, ChargeStore chargeStore) =>
{
    var matchingCharges = chargeStore.FindByEmail(customerEmail);
    return Results.Ok(matchingCharges);
});

app.Run();

public partial class Program { }
