namespace PaymentGateway.Api.Models.Responses;

public class BankPaymentResponse
{
    public required bool Authorized { get; init; }
    public string? AuthorizationCode { get; init; }
}