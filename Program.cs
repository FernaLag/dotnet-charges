using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ChargeStore>();
builder.Services.AddSingleton<PaymentProcessor>();
builder.Services.AddSingleton<AuditLog>();
var app = builder.Build();

// ============================================================
//  Charges API — small payments service
// ============================================================

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

// Expose Program to the test project (WebApplicationFactory<Program>)
public partial class Program { }

// ============================================================
//  Domain
// ============================================================

public record ChargeRequest(
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("customerEmail")] string CustomerEmail,
    [property: JsonPropertyName("cardToken")] string CardToken
);

public record Charge(
    string Id,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string Status,
    DateTime CreatedAt
);

// ============================================================
//  Store
// ============================================================

public class ChargeStore
{
    private readonly ConcurrentDictionary<string, Lazy<Task<Charge>>> _chargesByIdempotencyKey = new();
    private readonly ConcurrentDictionary<string, Charge> _chargesById = new();
    private readonly ConcurrentBag<Charge> _charges = new();

    public async Task<Charge> GetOrCreateAsync(string idempotencyKey, Func<Task<Charge>> createChargeAsync)
    {
        var lazyChargeTask = _chargesByIdempotencyKey.GetOrAdd(
            idempotencyKey,
            _ => new Lazy<Task<Charge>>(
                async () =>
                {
                    var createdCharge = await createChargeAsync();
                    _chargesById[createdCharge.Id] = createdCharge;
                    _charges.Add(createdCharge);
                    return createdCharge;
                },
                isThreadSafe: true));

        try
        {
            return await lazyChargeTask.Value;
        }
        catch
        {
            _chargesByIdempotencyKey.TryRemove(idempotencyKey, out _);
            throw;
        }
    }

    public Charge? GetById(string chargeId) => _chargesById.TryGetValue(chargeId, out var storedCharge) ? storedCharge : null;

    public List<Charge> FindByEmail(string customerEmail)
    {
        return _charges.Where(storedCharge => storedCharge.CustomerEmail == customerEmail).ToList();
    }
}

// ============================================================
//  Payment processor (calls a fake external service)
// ============================================================

public class PaymentProcessor
{
    public async Task<Charge> ChargeAsync(ChargeRequest chargeRequest)
    {
        // Simulate latency talking to the processor
        await Task.Delay(250);

        var chargeId = "ch_" + Guid.NewGuid().ToString("N")[..16];
        return new Charge(
            Id: chargeId,
            Amount: chargeRequest.Amount,
            Currency: chargeRequest.Currency,
            CustomerEmail: chargeRequest.CustomerEmail,
            Status: "succeeded",
            CreatedAt: DateTime.UtcNow
        );
    }
}

// ============================================================
//  Audit log
// ============================================================

public class AuditLog
{
    public async Task LogChargeAsync(Charge processedCharge, string customerEmail)
    {
        // Pretend we write to a SIEM
        await Task.Delay(50);
        Console.WriteLine($"[audit] charge={processedCharge.Id} amount={processedCharge.Amount} {processedCharge.Currency} email={customerEmail} cardToken=*** at={processedCharge.CreatedAt:O}");
    }
}
