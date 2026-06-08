# Charges API

Small payments service. Exposes `POST /charges`, `GET /charges/{id}`, and `GET /customers/search?email=`.

## Run

```bash
dotnet run
```

The API listens on `http://localhost:5000` when started with that URL.

```bash
dotnet run --urls http://localhost:5000
```

## Quick smoke test

```bash
curl -X POST http://localhost:5000/charges \
  -H "Content-Type: application/json" \
  -d '{"idempotencyKey":"k1","amount":12.50,"currency":"USD","customerEmail":"a@b.com","cardToken":"tok_visa"}'
```

## Behavior

`POST /charges` requires `idempotencyKey`, positive `amount`, `currency`, `customerEmail`, and `cardToken`.

Repeated requests with the same `idempotencyKey` return the same charge when the request data is the same. Reusing the same `idempotencyKey` with different request data returns `409 Conflict`.

Data is stored in memory, so charges and idempotency records are lost when the process stops.
