namespace PaymentGateway.Api.Exceptions;

public class AcquiringBankException : Exception
{
    public AcquiringBankException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}