using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Models.Domain;

public record IdempotencyRecord(string RequestFingerprint, IdempotencyState State, Guid? PaymentId);