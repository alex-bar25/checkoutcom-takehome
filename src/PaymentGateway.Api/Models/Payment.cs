using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Models;

/// <summary>
/// A payment as stored by the gateway. Deliberately holds only the last four card digits:
/// the full card number and CVV are never persisted.
/// </summary>
public record Payment(
    Guid Id,
    PaymentStatus Status,
    string CardNumberLastFour,
    int ExpiryMonth,
    int ExpiryYear,
    string Currency,
    int Amount);
