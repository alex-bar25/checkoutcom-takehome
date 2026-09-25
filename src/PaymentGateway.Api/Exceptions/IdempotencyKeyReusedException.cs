namespace PaymentGateway.Api.Exceptions;

public class IdempotencyKeyReusedException : Exception
{
    public IdempotencyKeyReusedException()
        : base("This idempotency key was already used for a different payment request.")
    {
    }
}