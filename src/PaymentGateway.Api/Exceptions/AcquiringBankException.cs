namespace PaymentGateway.Api.Exceptions;

public class AcquiringBankException : Exception
{
    public AcquiringBankException(string message, bool outcomeUnknown, Exception? innerException = null)
        : base(message, innerException)
    {
        OutcomeUnknown = outcomeUnknown;
    }

    public bool OutcomeUnknown { get; }
}