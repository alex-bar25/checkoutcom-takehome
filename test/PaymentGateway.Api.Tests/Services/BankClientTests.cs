using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

using RichardSzalay.MockHttp;

namespace PaymentGateway.Api.Tests.Services;

public class BankClientTests
{
    private const string BankUrl = "http://bank.test/payments";

    private static readonly BankPaymentRequest Request = new()
    {
        CardNumber = "2222405343248877",
        ExpiryDate = "04/2027",
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    private readonly MockHttpMessageHandler _bank = new();

    private BankClient CreateClient() =>
        new(new HttpClient(_bank) { BaseAddress = new Uri("http://bank.test") }, NullLogger<BankClient>.Instance);

    [Fact]
    public async Task SendsPaymentInTheBankFormat()
    {
        _bank.Expect(HttpMethod.Post, BankUrl)
            .WithContent("""{"card_number":"2222405343248877","expiry_date":"04/2027","currency":"GBP","amount":100,"cvv":"123"}""")
            .Respond("application/json", """{"authorized":true,"authorization_code":"abc"}""");

        await CreateClient().AuthorizeAsync(Request, CancellationToken.None);

        _bank.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ReturnsAuthorizedResultWithAuthorizationCode()
    {
        _bank.When(HttpMethod.Post, BankUrl)
            .Respond("application/json", """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}""");

        var result = await CreateClient().AuthorizeAsync(Request, CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", result.AuthorizationCode);
    }

    [Fact]
    public async Task ReturnsDeclinedResult()
    {
        _bank.When(HttpMethod.Post, BankUrl).Respond("application/json", """{"authorized":false,"authorization_code":""}""");

        var result = await CreateClient().AuthorizeAsync(Request, CancellationToken.None);

        Assert.False(result.Authorized);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ThrowsNotProcessedWhenBankRespondsWithError(HttpStatusCode statusCode)
    {
        _bank.When(HttpMethod.Post, BankUrl).Respond(statusCode, "application/json", "{}");

        var exception = await Assert.ThrowsAsync<AcquiringBankException>(() => CreateClient().AuthorizeAsync(Request, CancellationToken.None));
        Assert.False(exception.OutcomeUnknown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"authorization_code":"abc"}""")]
    public async Task ThrowsOutcomeUnknownWhenBankResponseIsMalformed(string json)
    {
        _bank.When(HttpMethod.Post, BankUrl).Respond("application/json", json);

        var exception = await Assert.ThrowsAsync<AcquiringBankException>(() => CreateClient().AuthorizeAsync(Request, CancellationToken.None));
        Assert.True(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task ThrowsNotProcessedWhenBankIsUnreachable()
    {
        _bank.When(HttpMethod.Post, BankUrl).Throw(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"));

        var exception = await Assert.ThrowsAsync<AcquiringBankException>(() => CreateClient().AuthorizeAsync(Request, CancellationToken.None));
        Assert.False(exception.OutcomeUnknown);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ThrowsOutcomeUnknownWhenConnectionDropsAfterRequestIsSent()
    {
        _bank.When(HttpMethod.Post, BankUrl).Throw(new HttpRequestException(HttpRequestError.ResponseEnded, "Response ended prematurely"));

        var exception = await Assert.ThrowsAsync<AcquiringBankException>(() => CreateClient().AuthorizeAsync(Request, CancellationToken.None));
        Assert.True(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task ThrowsOutcomeUnknownWhenBankTimesOut()
    {
        _bank.When(HttpMethod.Post, BankUrl).Throw(new TaskCanceledException("The request timed out"));

        var exception = await Assert.ThrowsAsync<AcquiringBankException>(() => CreateClient().AuthorizeAsync(Request, CancellationToken.None));
        Assert.True(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        _bank.When(HttpMethod.Post, BankUrl).Respond("application/json", """{"authorized":true,"authorization_code":"abc"}""");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient().AuthorizeAsync(Request, cancellation.Token));
    }
}