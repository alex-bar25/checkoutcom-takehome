using FluentValidation;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Tests.Services;

public class PaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly PostPaymentRequest ValidRequest = new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = 4,
        ExpiryYear = 2027,
        Currency = "GBP",
        Amount = 1050,
        Cvv = "123"
    };

    private readonly IBankClient _bankClient = Substitute.For<IBankClient>();
    private readonly PaymentsRepository _repository = new();
    private readonly IdempotencyStore _idempotencyStore = new();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _service = new PaymentService(
            new PostPaymentRequestValidator(new FakeTimeProvider(Now)),
            _bankClient,
            _repository,
            _idempotencyStore,
            NullLogger<PaymentService>.Instance);
    }

    [Fact]
    public async Task StoresAuthorizedPaymentWithAuthorizationCode()
    {
        BankResponds(authorized: true, authorizationCode: "auth-123");

        var payment = await _service.ProcessPaymentAsync(ValidRequest, null, CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("8877", payment.CardNumberLastFour);
        Assert.Equal(4, payment.ExpiryMonth);
        Assert.Equal(2027, payment.ExpiryYear);
        Assert.Equal("GBP", payment.Currency);
        Assert.Equal(1050, payment.Amount);
        Assert.Equal("auth-123", payment.AuthorizationCode);
        Assert.Equal(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public async Task StoresDeclinedPaymentWithoutAuthorizationCode()
    {
        BankResponds(authorized: false, authorizationCode: "");

        var payment = await _service.ProcessPaymentAsync(ValidRequest, null, CancellationToken.None);

        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Null(payment.AuthorizationCode);
        Assert.Equal(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public async Task SendsCardDetailsToBankWithFormattedExpiryDate()
    {
        BankResponds(authorized: true);

        await _service.ProcessPaymentAsync(ValidRequest, null, CancellationToken.None);

        await _bankClient.Received(1).AuthorizeAsync(
            Arg.Is<BankPaymentRequest>(sent =>
                sent.CardNumber == "2222405343248877" &&
                sent.ExpiryDate == "04/2027" &&
                sent.Currency == "GBP" &&
                sent.Amount == 1050 &&
                sent.Cvv == "123"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsInvalidRequestWithoutCallingBank()
    {
        var invalidRequest = ValidRequest with { CardNumber = "1234", Currency = "JPY" };

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _service.ProcessPaymentAsync(invalidRequest, null, CancellationToken.None));

        Assert.Equal(["CardNumber", "Currency"], exception.Errors.Select(error => error.PropertyName).Order());
        await _bankClient.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default);
    }

    [Fact]
    public async Task PropagatesBankFailure()
    {
        _bankClient.AuthorizeAsync(default!, default)
            .ThrowsAsyncForAnyArgs(new AcquiringBankException("Acquiring bank failed.", outcomeUnknown: false));

        await Assert.ThrowsAsync<AcquiringBankException>(() => _service.ProcessPaymentAsync(ValidRequest, null, CancellationToken.None));
    }

    [Fact]
    public async Task RetrievesProcessedPayment()
    {
        BankResponds(authorized: true);

        var payment = await _service.ProcessPaymentAsync(ValidRequest, null, CancellationToken.None);

        Assert.Equal(payment, _service.GetPayment(payment.Id));
        Assert.Null(_service.GetPayment(Guid.NewGuid()));
    }

    [Fact]
    public async Task ProcessesRepeatedRequestsWithoutIdempotencyKeyAsSeparatePayments()
    {
        BankResponds(authorized: true);

        var first = await _service.ProcessPaymentAsync(ValidRequest, null, CancellationToken.None);
        var second = await _service.ProcessPaymentAsync(ValidRequest, null, CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        await _bankClient.ReceivedWithAnyArgs(2).AuthorizeAsync(default!, default);
    }

    [Fact]
    public async Task ReplaysPaymentForRepeatedIdempotencyKeyWithoutCallingBankAgain()
    {
        BankResponds(authorized: true);

        var first = await _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None);
        var replayed = await _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None);

        Assert.Equal(first, replayed);
        await _bankClient.ReceivedWithAnyArgs(1).AuthorizeAsync(default!, default);
    }

    [Fact]
    public async Task RejectsIdempotencyKeyReusedForDifferentRequest()
    {
        BankResponds(authorized: true);
        await _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None);

        await Assert.ThrowsAsync<IdempotencyKeyReusedException>(() =>
            _service.ProcessPaymentAsync(ValidRequest with { Amount = 9999 }, "key-1", CancellationToken.None));

        await _bankClient.ReceivedWithAnyArgs(1).AuthorizeAsync(default!, default);
    }

    [Fact]
    public async Task RejectsConcurrentRequestWithSameIdempotencyKey()
    {
        var bankResponse = new TaskCompletionSource<BankPaymentResponse>();
        _bankClient.AuthorizeAsync(default!, default).ReturnsForAnyArgs(bankResponse.Task);

        var first = _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None);
        await Assert.ThrowsAsync<IdempotencyKeyInUseException>(() =>
            _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None));

        bankResponse.SetResult(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" });
        Assert.Equal(PaymentStatus.Authorized, (await first).Status);
        await _bankClient.ReceivedWithAnyArgs(1).AuthorizeAsync(default!, default);
    }

    [Fact]
    public async Task ReleasesIdempotencyKeyWhenRequestIsRejected()
    {
        BankResponds(authorized: true);
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ProcessPaymentAsync(ValidRequest with { Currency = "JPY" }, "key-1", CancellationToken.None));

        var payment = await _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
    }

    [Fact]
    public async Task ReleasesIdempotencyKeyWhenBankDidNotProcessPayment()
    {
        _bankClient.AuthorizeAsync(default!, default).ReturnsForAnyArgs(
            _ => throw new AcquiringBankException("Acquiring bank failed.", outcomeUnknown: false),
            _ => Task.FromResult(new BankPaymentResponse { Authorized = true, AuthorizationCode = "auth-123" }));
        await Assert.ThrowsAsync<AcquiringBankException>(() =>
            _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None));

        var payment = await _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        await _bankClient.ReceivedWithAnyArgs(2).AuthorizeAsync(default!, default);
    }

    [Fact]
    public async Task KeepsIdempotencyKeyLockedWhenBankOutcomeIsUnknown()
    {
        _bankClient.AuthorizeAsync(default!, default)
            .ThrowsAsyncForAnyArgs(new AcquiringBankException("Acquiring bank timed out.", outcomeUnknown: true));
        await Assert.ThrowsAsync<AcquiringBankException>(() =>
            _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None));

        var exception = await Assert.ThrowsAsync<AcquiringBankException>(() =>
            _service.ProcessPaymentAsync(ValidRequest, "key-1", CancellationToken.None));

        Assert.True(exception.OutcomeUnknown);
        await _bankClient.ReceivedWithAnyArgs(1).AuthorizeAsync(default!, default);
    }

    private void BankResponds(bool authorized, string authorizationCode = "0bb07405-6d44-4b50-a14f-7ae0beff13ad") =>
        _bankClient.AuthorizeAsync(default!, default)
            .ReturnsForAnyArgs(new BankPaymentResponse { Authorized = authorized, AuthorizationCode = authorizationCode });
}