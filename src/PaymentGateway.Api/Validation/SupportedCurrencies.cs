namespace PaymentGateway.Api.Validation;

public static class SupportedCurrencies
{
    public static readonly IReadOnlyList<string> Codes = ["GBP", "USD", "EUR"];

    public static bool IsSupported(string currency) => Codes.Contains(currency, StringComparer.Ordinal);
}