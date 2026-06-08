using ChargesApi.Domain;

namespace ChargesApi.Services;

public static class ChargeRequestValidator
{
    public static ChargeValidationResult Validate(ChargeRequest chargeRequest)
    {
        if (string.IsNullOrWhiteSpace(chargeRequest.IdempotencyKey))
            return ChargeValidationResult.Invalid("idempotencyKey is required");

        if (chargeRequest.Amount <= 0)
            return ChargeValidationResult.Invalid("amount must be greater than zero");

        if (string.IsNullOrWhiteSpace(chargeRequest.Currency))
            return ChargeValidationResult.Invalid("currency is required");

        if (string.IsNullOrWhiteSpace(chargeRequest.CustomerEmail))
            return ChargeValidationResult.Invalid("customerEmail is required");

        if (string.IsNullOrWhiteSpace(chargeRequest.CardToken))
            return ChargeValidationResult.Invalid("cardToken is required");

        return ChargeValidationResult.Valid();
    }
}

public record ChargeValidationResult(bool IsValid, string? ErrorMessage)
{
    public static ChargeValidationResult Valid() => new(true, null);

    public static ChargeValidationResult Invalid(string errorMessage) => new(false, errorMessage);
}
