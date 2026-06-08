using System.Collections.Concurrent;
using ChargesApi.Domain;

namespace ChargesApi.Stores;

public class ChargeStore
{
    private readonly ConcurrentDictionary<string, IdempotentChargeCreation> _chargesByIdempotencyKey = new();
    private readonly ConcurrentDictionary<string, Charge> _chargesById = new();
    private readonly ConcurrentBag<Charge> _allCharges = new();

    public async Task<ChargeStoreResult> GetOrCreateAsync(string idempotencyKey, string chargeRequestFingerprint, Func<Task<Charge>> createChargeAsync)
    {
        var chargeCreation = new IdempotentChargeCreation(
            chargeRequestFingerprint,
            new Lazy<Task<Charge>>(
                async () =>
                {
                    var createdCharge = await createChargeAsync();
                    _chargesById[createdCharge.Id] = createdCharge;
                    _allCharges.Add(createdCharge);
                    return createdCharge;
                },
                isThreadSafe: true));
        var storedChargeCreation = _chargesByIdempotencyKey.GetOrAdd(idempotencyKey, chargeCreation);

        if (!string.Equals(storedChargeCreation.ChargeRequestFingerprint, chargeRequestFingerprint, StringComparison.Ordinal))
            return new ChargeStoreResult(null, true);

        try
        {
            var storedCharge = await storedChargeCreation.Charge.Value;
            return new ChargeStoreResult(storedCharge, false);
        }
        catch
        {
            var storedChargeCreationEntry = new KeyValuePair<string, IdempotentChargeCreation>(idempotencyKey, storedChargeCreation);
            ((ICollection<KeyValuePair<string, IdempotentChargeCreation>>)_chargesByIdempotencyKey).Remove(storedChargeCreationEntry);
            throw;
        }
    }

    public Charge? GetById(string chargeId) => _chargesById.TryGetValue(chargeId, out var storedCharge) ? storedCharge : null;

    public List<Charge> FindByEmail(string customerEmail)
    {
        return _allCharges
            .Where(storedCharge => string.Equals(storedCharge.CustomerEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private sealed record IdempotentChargeCreation(string ChargeRequestFingerprint, Lazy<Task<Charge>> Charge);
}

public record ChargeStoreResult(Charge? Charge, bool HasIdempotencyConflict);
