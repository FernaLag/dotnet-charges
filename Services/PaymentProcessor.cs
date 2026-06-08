using ChargesApi.Domain;

namespace ChargesApi.Services;

public class PaymentProcessor
{
    public async Task<Charge> ChargeAsync(ChargeRequest chargeRequest)
    {
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
