using System.Text.Json;

using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

using Polly;

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
                throw new AcquiringBankException($"Acquiring bank responded with status code {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<BankPaymentResponse>(SerializerOptions, cancellationToken)
                ?? throw new AcquiringBankException("Acquiring bank returned an empty response.");
        }
        catch (Exception exception) when (IsBankFailure(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Call to acquiring bank failed");
            throw new AcquiringBankException("Call to acquiring bank failed.", exception);
        }
    }

    private static bool IsBankFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or ExecutionRejectedException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}