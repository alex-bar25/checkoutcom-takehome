using FluentValidation.TestHelper;

using Microsoft.Extensions.Time.Testing;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Tests.Validation;

public class PostPaymentRequestValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly PostPaymentRequest ValidRequest = new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = 4,
        ExpiryYear = 2027,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    private readonly PostPaymentRequestValidator _validator = new(new FakeTimeProvider(Now));

    [Fact]
    public void AcceptsValidRequest()
    {
        _validator.TestValidate(ValidRequest).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2222405343248")]
    [InlineData("22224053432488770000")]
    [InlineData("2222 4053 4324 8877")]
    [InlineData("222240534324887A")]
    [InlineData("-222405343248877")]
    public void RejectsInvalidCardNumber(string? cardNumber)
    {
        var result = _validator.TestValidate(ValidRequest with { CardNumber = cardNumber });

        result.ShouldHaveValidationErrorFor(r => r.CardNumber).Only();
    }

    [Theory]
    [InlineData("22224053432488")]
    [InlineData("2222405343248877123")]
    public void AcceptsCardNumberAtLengthBoundaries(string cardNumber)
    {
        _validator.TestValidate(ValidRequest with { CardNumber = cardNumber }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(13)]
    public void RejectsInvalidExpiryMonth(int? expiryMonth)
    {
        var result = _validator.TestValidate(ValidRequest with { ExpiryMonth = expiryMonth });

        result.ShouldHaveValidationErrorFor(r => r.ExpiryMonth).Only();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void AcceptsExpiryMonthAtBoundaries(int expiryMonth)
    {
        _validator.TestValidate(ValidRequest with { ExpiryMonth = expiryMonth }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void RejectsMissingExpiryYear()
    {
        var result = _validator.TestValidate(ValidRequest with { ExpiryYear = null });

        result.ShouldHaveValidationErrorFor(r => r.ExpiryYear).Only();
    }

    [Theory]
    [InlineData(5, 2026)]
    [InlineData(12, 2025)]
    [InlineData(7, 2025)]
    public void RejectsExpiryInThePast(int expiryMonth, int expiryYear)
    {
        var result = _validator.TestValidate(ValidRequest with { ExpiryMonth = expiryMonth, ExpiryYear = expiryYear });

        result.ShouldHaveValidationErrorFor(r => r.ExpiryYear).Only();
    }

    [Theory]
    [InlineData(6, 2026)]
    [InlineData(1, 2027)]
    public void AcceptsExpiryFromCurrentMonthOnwards(int expiryMonth, int expiryYear)
    {
        var result = _validator.TestValidate(ValidRequest with { ExpiryMonth = expiryMonth, ExpiryYear = expiryYear });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GB")]
    [InlineData("GBPP")]
    [InlineData("gbp")]
    [InlineData("JPY")]
    public void RejectsInvalidCurrency(string? currency)
    {
        var result = _validator.TestValidate(ValidRequest with { Currency = currency });

        result.ShouldHaveValidationErrorFor(r => r.Currency).Only();
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void AcceptsSupportedCurrencies(string currency)
    {
        _validator.TestValidate(ValidRequest with { Currency = currency }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidAmount(int? amount)
    {
        var result = _validator.TestValidate(ValidRequest with { Amount = amount });

        result.ShouldHaveValidationErrorFor(r => r.Amount).Only();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    public void RejectsInvalidCvv(string? cvv)
    {
        var result = _validator.TestValidate(ValidRequest with { Cvv = cvv });

        result.ShouldHaveValidationErrorFor(r => r.Cvv).Only();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    [InlineData("012")]
    public void AcceptsValidCvv(string cvv)
    {
        _validator.TestValidate(ValidRequest with { Cvv = cvv }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ReportsOneErrorPerInvalidField()
    {
        var result = _validator.TestValidate(new PostPaymentRequest());

        Assert.Equal(6, result.Errors.Count);
    }
}
