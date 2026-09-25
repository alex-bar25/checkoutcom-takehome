using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Services;

public class PaymentsRepositoryTests
{
    private readonly PaymentsRepository _repository = new();

    [Fact]
    public void ReturnsAddedPayment()
    {
        var payment = new Payment(Guid.NewGuid(), PaymentStatus.Declined, "8877", 4, 2030, "USD", 100, null);

        _repository.Add(payment);

        Assert.Equal(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public void ReturnsNullForUnknownId()
    {
        Assert.Null(_repository.Get(Guid.NewGuid()));
    }
}