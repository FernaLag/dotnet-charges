using ChargesApi.Domain;

namespace ChargesApi.Services;

public class AuditLog
{
    public async Task LogChargeAsync(Charge processedCharge, string customerEmail)
    {
        await Task.Delay(50);
        Console.WriteLine($"[audit] charge={processedCharge.Id} amount={processedCharge.Amount} {processedCharge.Currency} email={customerEmail} cardToken=*** at={processedCharge.CreatedAt:O}");
    }
}
