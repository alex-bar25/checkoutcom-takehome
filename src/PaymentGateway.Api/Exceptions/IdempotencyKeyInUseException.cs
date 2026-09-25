namespace PaymentGateway.Api.Exceptions;

public class IdempotencyKeyInUseException : Exception
{
    public IdempotencyKeyInUseException()
        : base("A request with this idempotency key is still being processed.")
    {
    }
}