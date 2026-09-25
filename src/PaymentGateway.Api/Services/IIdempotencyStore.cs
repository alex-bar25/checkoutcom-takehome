using PaymentGateway.Api.Models.Domain;

namespace PaymentGateway.Api.Services;

public interface IIdempotencyStore
{
    IdempotencyRecord? TryBegin(string key, string requestFingerprint);
    void Complete(string key, Guid paymentId);
    void MarkOutcomeUnknown(string key);
    void Release(string key);
}