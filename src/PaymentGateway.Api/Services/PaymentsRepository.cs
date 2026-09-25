using System.Collections.Concurrent;

using PaymentGateway.Api.Models.Domain;

namespace PaymentGateway.Api.Services;

/// <summary>
/// In-memory test double standing in for a real database. Registered as a singleton,
/// so it must be safe for concurrent requests.
/// </summary>
public class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, Payment> _payments = new();

    public void Add(Payment payment)
    {
        if (!_payments.TryAdd(payment.Id, payment))
        {
            throw new InvalidOperationException($"A payment with id {payment.Id} already exists.");
        }
    }

    public Payment? Get(Guid id)
    {
        return _payments.GetValueOrDefault(id);
    }
}