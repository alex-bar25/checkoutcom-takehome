using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Configuration;

public class BankOptions
{
    public const string SectionName = "Bank";

    [Required]
    public Uri? BaseAddress { get; init; }

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}