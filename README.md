# Payment Gateway

An ASP.NET Core API that lets a merchant process a card payment through an acquiring bank and retrieve it later. The bank is the Mountebank simulator provided in `imposters/`.

## Running it

Requirements: .NET 10 SDK and Docker.

```bash
docker compose up -d                              # bank simulator on :8080
dotnet run --project src/PaymentGateway.Api       # API on https://localhost:7092, Swagger at /swagger
```

If the local HTTPS certificate isn't trusted yet, run `dotnet dev-certs https --trust` once.

```bash
dotnet test test/PaymentGateway.Api.Tests         # unit + integration, no Docker needed
dotnet test                                       # everything, including end-to-end (needs Docker running)
```

## API

`POST /api/payments`

```json
{
  "cardNumber": "2222405343248877",
  "expiryMonth": 4,
  "expiryYear": 2027,
  "currency": "GBP",
  "amount": 1050,
  "cvv": "123"
}
```

| Outcome | Response |
|---|---|
| Authorized / Declined by the bank | `201` with the payment and a `Location` header |
| Invalid request (Rejected) | `422` `{ "status": "Rejected", "errors": { "cardNumber": [...] } }`, bank not called |
| Bank did not process the payment | `502`, safe to retry |
| Bank did not answer in time | `504`, outcome unknown |

`GET /api/payments/{id}` returns `200` with the payment or `404`.

A payment response contains `id`, `status`, `cardNumberLastFour`, `expiryMonth`, `expiryYear`, `currency` and `amount`.

## Design decisions

**Structure.** Controller (HTTP only) → `PaymentService` (validate, call the bank, store) → `BankClient` and `PaymentsRepository`. Folders follow the scaffold's layout. There is no mediator or mapping library; two endpoints don't need them.

**Validation.** FluentValidation. Request fields are nullable so a missing value can be told apart from `0`, and JSON is parsed strictly (`"1050"` is not a number, `1234` is not a card number). Malformed JSON gets the same `Rejected` response as a failed rule, so a merchant only has to handle one shape. `422` rather than `400` because the request is well-formed HTTP but the payment data is invalid, which is also what Checkout.com's own API does.

**Card data.** Only the last four digits are stored, as a string so leading zeros survive. The full card number and CVV are never stored, logged or returned, and tests check responses and logs for them. Payment responses are sent with `Cache-Control: no-store`.

**Bank integration.** A typed `HttpClient` with the standard .NET resilience handler for timeouts and circuit breaking. Retries are disabled for the payment POST: retrying a charge the bank may already have processed could take the shopper's money twice. The bank address and timeout come from configuration and are validated at startup.

**502 vs 504.** When the bank fails, the merchant needs to know whether it is safe to retry. An error status or a refused connection means the payment definitely wasn't processed (`502`). A timeout or a connection dropped mid-response means the bank may have charged the card (`504`), so the merchant shouldn't blindly retry.

**.NET 10.** The scaffold targeted .NET 8. I moved to .NET 10 (the current LTS and the runtime I had) and updated the packages.

## Assumptions

- Amount must be greater than zero. The brief only says it must be an integer.
- A card is valid until the end of its expiry month, compared in UTC. There is no upper limit on the expiry year.
- Supported currencies are GBP, USD and EUR, upper case only.
- No Luhn check on the card number. The brief doesn't ask for one and the simulator's test cards wouldn't pass it.
- Declined payments are stored and retrievable. Rejected payments are not stored and have no id.
- A `400` from the bank means the gateway sent a bad request. It is treated as "not processed" (`502`); it can't happen while validation matches the bank's rules.

## Testing

- **Unit** (`PaymentGateway.Api.Tests`): validation rules, `PaymentService` with a substituted bank, `BankClient` against a mocked HTTP handler including every failure type.
- **Integration** (same project): the full HTTP pipeline in-process with `WebApplicationFactory`, covering status codes, error shapes, headers, and that card data never appears in responses or logs.
- **End-to-end** (`PaymentGateway.Api.EndToEndTests`): the real gateway against the real simulator, started by Testcontainers with the same image and the unchanged `imposters/` as `docker-compose.yml`. It covers every card ending, retrieval, the `503` case, and uses the simulator's request count to check that rejected payments never reach the bank.

The `504` path can't be triggered through this simulator, so it is covered by unit and integration tests only. The brief mentions that Mountebank is usually configured through its API in test setup; I kept the provided imposter file unchanged, as the scaffold asks.

I didn't include a load test. Against a local Mountebank it would mostly measure the simulator. In a real setup I'd run k6 against a deployed environment with a bank stub that has realistic latency, and track p95/p99 latency and error rate against an SLO.

## What production would need next

- **Idempotency.** An `Idempotency-Key` header so a merchant retrying after a network error can't charge the shopper twice. I left it out to keep to the brief's functional requirements. The design: the same key and request replays the original payment without calling the bank; a concurrent request with the same key gets `409`; the same key with a different request gets `422`; the key is released if the payment was rejected or definitely not processed, and stays locked if the outcome is unknown. Keys are scoped per merchant, expire after about 24 hours, and live in a shared store such as Redis. Requests are matched on an HMAC of the body so no card data is kept.
- **Merchant authentication.** Payments should be scoped to the merchant that created them; right now any caller can read any payment by id.
- **Persistence.** A real database, recording the payment as pending before calling the bank so a timeout leaves a record that a reconciliation job can resolve with the bank.
- **HTTPS only.** Reject plain HTTP rather than redirect: a redirect happens after the card data has already been sent in clear.
- **Card data.** Tokenization, so most of the system never handles card numbers.
- **Observability.** Metrics on authorization rate, bank latency and error rate, and tracing across the bank call.
- **Rate limiting** per merchant.
