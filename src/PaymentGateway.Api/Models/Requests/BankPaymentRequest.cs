namespace PaymentGateway.Api.Models.Requests;

public class BankPaymentRequest
{
    public required string CardNumber { get; init; }
    public required string ExpiryDate { get; init; }
    public required string Currency { get; init; }
    public required int Amount { get; init; }
    public required string Cvv { get; init; }
}