using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChargesApi.Domain;

namespace ChargesApi.Services;

public static class ChargeRequestFingerprint
{
    public static string Create(ChargeRequest chargeRequest)
    {
        var chargeRequestData = new
        {
            chargeRequest.Amount,
            chargeRequest.Currency,
            chargeRequest.CustomerEmail,
            chargeRequest.CardToken
        };
        var chargeRequestJson = JsonSerializer.Serialize(chargeRequestData);
        var chargeRequestBytes = Encoding.UTF8.GetBytes(chargeRequestJson);
        var chargeRequestHashBytes = SHA256.HashData(chargeRequestBytes);

        return Convert.ToHexString(chargeRequestHashBytes);
    }
}
