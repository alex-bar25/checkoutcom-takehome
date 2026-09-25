using System.Security.Cryptography;
using System.Text.Json;

using FluentValidation;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class PaymentService : IPaymentService
{
    private static readonly byte[] FingerprintKey = RandomNumberGenerator.GetBytes(32);

    private readonly IValidator<PostPaymentRequest> _validator;
    private readonly IBankClient _bankClient;
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IValidator<PostPaymentRequest> validator,
        IBankClient bankClient,
        IPaymentsRepository paymentsRepository,
        IIdempotencyStore idempotencyStore,
        ILogger<PaymentService> logger)
    {
        _validator = validator;
        _bankClient = bankClient;
        _paymentsRepository = paymentsRepository;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
    }

    public async Task<Payment> ProcessPaymentAsync(PostPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (idempotencyKey is null)
        {
            return await ProcessNewPaymentAsync(request, cancellationToken);
        }

        var requestFingerprint = Fingerprint(request);
        var existing = _idempotencyStore.TryBegin(idempotencyKey, requestFingerprint);

        if (existing is not null)
        {
            return Replay(existing, requestFingerprint);
        }

        try
        {
            var payment = await ProcessNewPaymentAsync(request, cancellationToken);
            _idempotencyStore.Complete(idempotencyKey, payment.Id);
            return payment;
        }
        catch (Exception exception) when (exception is ValidationException or AcquiringBankException { OutcomeUnknown: false })
        {
            _idempotencyStore.Release(idempotencyKey);
            throw;
        }
        catch
        {
            _idempotencyStore.MarkOutcomeUnknown(idempotencyKey);
            throw;
        }
    }

    public Payment? GetPayment(Guid id)
    {
        return _paymentsRepository.Get(id);
    }

    private async Task<Payment> ProcessNewPaymentAsync(PostPaymentRequest request, CancellationToken cancellationToken)
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

    private Payment Replay(IdempotencyRecord record, string requestFingerprint)
    {
        if (record.RequestFingerprint != requestFingerprint)
        {
            throw new IdempotencyKeyReusedException();
        }

        switch (record.State)
        {
            case IdempotencyState.Completed:
                var payment = _paymentsRepository.Get(record.PaymentId!.Value)!;
                _logger.LogInformation("Replaying payment {PaymentId} for a repeated idempotency key", payment.Id);
                return payment;
            case IdempotencyState.OutcomeUnknown:
                throw new AcquiringBankException("The outcome of the original request with this idempotency key is unknown.", outcomeUnknown: true);
            default:
                throw new IdempotencyKeyInUseException();
        }
    }

    private static string Fingerprint(PostPaymentRequest request) =>
        Convert.ToHexString(HMACSHA256.HashData(FingerprintKey, JsonSerializer.SerializeToUtf8Bytes(request)));
}