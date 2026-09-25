namespace PaymentGateway.Api.Enums;

public enum IdempotencyState
{
    InProgress,
    Completed,
    OutcomeUnknown
}