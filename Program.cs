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
    var chargeValidationResult = ChargeRequestValidator.Validate(chargeRequest);
    if (!chargeValidationResult.IsValid)
        return Results.BadRequest(new { error = chargeValidationResult.ErrorMessage });

    var chargeRequestFingerprint = ChargeRequestFingerprint.Create(chargeRequest);
    var chargeStoreResult = await chargeStore.GetOrCreateAsync(chargeRequest.IdempotencyKey, chargeRequestFingerprint, async () =>
    {
        var createdCharge = await paymentProcessor.ChargeAsync(chargeRequest, chargeRequest.IdempotencyKey);
        _ = auditLog.LogChargeAsync(createdCharge, chargeRequest.CustomerEmail);
        return createdCharge;
    });

    if (chargeStoreResult.HasIdempotencyConflict || chargeStoreResult.Charge is not Charge processedCharge)
        return Results.Conflict(new { error = "idempotencyKey was already used with different request data" });

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
