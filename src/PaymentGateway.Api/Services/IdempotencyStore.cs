using System.Collections.Concurrent;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Domain;

namespace PaymentGateway.Api.Services;

public class IdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new(StringComparer.Ordinal);

    public IdempotencyRecord? TryBegin(string key, string requestFingerprint)
    {
        var record = new IdempotencyRecord(requestFingerprint, IdempotencyState.InProgress, null);
        var existing = _records.GetOrAdd(key, record);

        return ReferenceEquals(existing, record) ? null : existing;
    }

    public void Complete(string key, Guid paymentId)
    {
        _records[key] = _records[key] with { State = IdempotencyState.Completed, PaymentId = paymentId };
    }

    public void MarkOutcomeUnknown(string key)
    {
        _records[key] = _records[key] with { State = IdempotencyState.OutcomeUnknown };
    }

    public void Release(string key)
    {
        _records.TryRemove(key, out _);
    }
}