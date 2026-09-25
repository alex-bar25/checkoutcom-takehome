using Microsoft.AspNetCore.Mvc.ModelBinding;

using PaymentGateway.Api.Enums;

namespace PaymentGateway.Api.Models.Responses;

public class RejectedPaymentResponse
{
    public PaymentStatus Status => PaymentStatus.Rejected;
    public required IDictionary<string, string[]> Errors { get; init; }

    public static RejectedPaymentResponse FromModelState(ModelStateDictionary modelState)
    {
        var invalidFields = modelState
            .Where(entry => entry.Key.StartsWith("$.") && entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key[2..], _ => new[] { "The value has an invalid format." });

        return new RejectedPaymentResponse
        {
            Errors = invalidFields.Count > 0
                ? invalidFields
                : new Dictionary<string, string[]> { ["request"] = ["The request body must be valid JSON."] }
        };
    }
}