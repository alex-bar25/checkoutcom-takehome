using PaymentGateway.Api.Models.Domain;

namespace PaymentGateway.Api.Services;

public interface IPaymentsRepository
{
    void Add(Payment payment);
    Payment? Get(Guid id);
}