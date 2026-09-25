using FluentValidation;

using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Validation;

public class PostPaymentRequestValidator : AbstractValidator<PostPaymentRequest>
{
    private readonly TimeProvider _timeProvider;

    public PostPaymentRequestValidator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.CardNumber)
            .NotEmpty()
            .Length(14, 19)
            .Must(BeNumeric).WithMessage("'{PropertyName}' must only contain numeric characters.");

        RuleFor(r => r.ExpiryMonth)
            .NotNull()
            .InclusiveBetween(1, 12);

        RuleFor(r => r.ExpiryYear)
            .NotNull()
            .Must((request, expiryYear) => IsNotExpired(request.ExpiryMonth!.Value, expiryYear!.Value))
            .WithMessage("The card expiry date must be in the future.")
            .When(r => r.ExpiryMonth is >= 1 and <= 12, ApplyConditionTo.CurrentValidator);

        RuleFor(r => r.Currency)
            .NotEmpty()
            .Length(3)
            .Must(SupportedCurrencies.IsSupported)
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", SupportedCurrencies.Codes)}.");

        RuleFor(r => r.Amount)
            .NotNull()
            .GreaterThan(0);

        RuleFor(r => r.Cvv)
            .NotEmpty()
            .Length(3, 4)
            .Must(BeNumeric).WithMessage("'{PropertyName}' must only contain numeric characters.");
    }

    private static bool BeNumeric(string? value) => value is not null && value.All(char.IsAsciiDigit);

    private bool IsNotExpired(int expiryMonth, int expiryYear)
    {
        var today = _timeProvider.GetUtcNow();
        return expiryYear > today.Year || (expiryYear == today.Year && expiryMonth >= today.Month);
    }
}
