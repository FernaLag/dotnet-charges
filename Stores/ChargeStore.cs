using System.Collections.Concurrent;
using ChargesApi.Domain;

namespace ChargesApi.Stores;

public class ChargeStore
{
    private readonly ConcurrentDictionary<string, Lazy<Task<Charge>>> _chargesByIdempotencyKey = new();
    private readonly ConcurrentDictionary<string, Charge> _chargesById = new();
    private readonly ConcurrentBag<Charge> _allCharges = new();

    public async Task<Charge> GetOrCreateAsync(string idempotencyKey, Func<Task<Charge>> createChargeAsync)
    {
        var chargeCreation = new Lazy<Task<Charge>>(
            async () =>
            {
                var createdCharge = await createChargeAsync();
                _chargesById[createdCharge.Id] = createdCharge;
                _allCharges.Add(createdCharge);
                return createdCharge;
            },
            isThreadSafe: true);
        var storedChargeCreation = _chargesByIdempotencyKey.GetOrAdd(idempotencyKey, chargeCreation);

        try
        {
            return await storedChargeCreation.Value;
        }
        catch
        {
            var storedChargeCreationEntry = new KeyValuePair<string, Lazy<Task<Charge>>>(idempotencyKey, storedChargeCreation);
            ((ICollection<KeyValuePair<string, Lazy<Task<Charge>>>>)_chargesByIdempotencyKey).Remove(storedChargeCreationEntry);
            throw;
        }
    }

    public Charge? GetById(string chargeId) => _chargesById.TryGetValue(chargeId, out var storedCharge) ? storedCharge : null;

    public List<Charge> FindByEmail(string customerEmail)
    {
        return _allCharges.Where(storedCharge => storedCharge.CustomerEmail == customerEmail).ToList();
    }
}
