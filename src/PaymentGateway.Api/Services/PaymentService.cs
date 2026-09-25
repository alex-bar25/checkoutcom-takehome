using FluentValidation;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class PaymentService : IPaymentService
{
    private readonly IValidator<PostPaymentRequest> _validator;
    private readonly IBankClient _bankClient;
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IValidator<PostPaymentRequest> validator,
        IBankClient bankClient,
        IPaymentsRepository paymentsRepository,
        ILogger<PaymentService> logger)
    {
        _validator = validator;
        _bankClient = bankClient;
        _paymentsRepository = paymentsRepository;
        _logger = logger;
    }

    public async Task<Payment> ProcessPaymentAsync(PostPaymentRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            _logger.LogInformation("Payment rejected due to invalid fields {InvalidFields}", validationResult.ToDictionary().Keys);
            throw new ValidationException(validationResult.Errors);
        }

        var bankResponse = await _bankClient.AuthorizeAsync(new BankPaymentRequest
        {
            CardNumber = request.CardNumber!,
            ExpiryDate = $"{request.ExpiryMonth:D2}/{request.ExpiryYear}",
            Currency = request.Currency!,
            Amount = request.Amount!.Value,
            Cvv = request.Cvv!
        }, cancellationToken);

        var payment = new Payment(
            Id: Guid.NewGuid(),
            Status: bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
            CardNumberLastFour: request.CardNumber![^4..],
            ExpiryMonth: request.ExpiryMonth!.Value,
            ExpiryYear: request.ExpiryYear!.Value,
            Currency: request.Currency!,
            Amount: request.Amount.Value,
            AuthorizationCode: bankResponse.Authorized ? bankResponse.AuthorizationCode : null);

        _paymentsRepository.Add(payment);
        _logger.LogInformation("Payment {PaymentId} processed with status {Status}", payment.Id, payment.Status);

        return payment;
    }

    public Payment? GetPayment(Guid id)
    {
        return _paymentsRepository.Get(id);
    }
}