using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public interface IPaymentService
{
    Task<Payment> ProcessPaymentAsync(PostPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Payment? GetPayment(Guid id);
}