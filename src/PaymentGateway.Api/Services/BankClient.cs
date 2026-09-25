using System.Text.Json;

using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

using Polly;
using Polly.Timeout;

namespace PaymentGateway.Api.Services;

public class BankClient : IBankClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<BankClient> _logger;

    public BankClient(HttpClient httpClient, ILogger<BankClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BankPaymentResponse> AuthorizeAsync(BankPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("payments", request, SerializerOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Acquiring bank responded with status code {StatusCode}", (int)response.StatusCode);
                throw new AcquiringBankException($"Acquiring bank responded with status code {(int)response.StatusCode}.", outcomeUnknown: false);
            }

            return await response.Content.ReadFromJsonAsync<BankPaymentResponse>(SerializerOptions, cancellationToken)
                ?? throw new AcquiringBankException("Acquiring bank returned an empty response.", outcomeUnknown: true);
        }
        catch (Exception exception) when (IsBankFailure(exception, cancellationToken))
        {
            var outcomeUnknown = IsOutcomeUnknown(exception);

            _logger.LogWarning(exception, "Call to acquiring bank failed, outcome unknown: {OutcomeUnknown}", outcomeUnknown);
            throw new AcquiringBankException("Call to acquiring bank failed.", outcomeUnknown, exception);
        }
    }

    private static bool IsBankFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or ExecutionRejectedException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static bool IsOutcomeUnknown(Exception exception) => exception switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError } => false,
        ExecutionRejectedException and not TimeoutRejectedException => false,
        _ => true
    };
}