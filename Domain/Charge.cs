namespace ChargesApi.Domain;

public record Charge(
    string Id,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string Status,
    DateTime CreatedAt
);
