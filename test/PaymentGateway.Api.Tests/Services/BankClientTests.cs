using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Support;

namespace PaymentGateway.Api.Tests.Services;

public class BankClientTests
{
    private static readonly BankPaymentRequest Request = new()
    {
        CardNumber = "2222405343248877",
        ExpiryDate = "04/2027",
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    private static BankClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://bank.test") }, NullLogger<BankClient>.Instance);

    [Fact]
    public async Task SendsPaymentInTheBankFormat()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"abc"}""");

        await CreateClient(handler).AuthorizeAsync(Request, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("http://bank.test/payments", sent.Uri?.ToString());

        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal("2222405343248877", body.RootElement.GetProperty("card_number").GetString());
        Assert.Equal("04/2027", body.RootElement.GetProperty("expiry_date").GetString());
        Assert.Equal("GBP", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal(100, body.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("123", body.RootElement.GetProperty("cvv").GetString());
    }

    [Fact]
    public async Task ReturnsAuthorizedResultWithAuthorizationCode()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK,
            """{"authorized":true,"authorization_code":"0bb07405-6d44-4b50-a14f-7ae0beff13ad"}""");

        var result = await CreateClient(handler).AuthorizeAsync(Request, CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.Equal("0bb07405-6d44-4b50-a14f-7ae0beff13ad", result.AuthorizationCode);
    }

    [Fact]
    public async Task ReturnsDeclinedResult()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"authorized":false,"authorization_code":""}""");

        var result = await CreateClient(handler).AuthorizeAsync(Request, CancellationToken.None);

        Assert.False(result.Authorized);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ThrowsWhenBankRespondsWithError(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Returning(statusCode, "{}");

        await Assert.ThrowsAsync<AcquiringBankException>(() => CreateClient(handler).AuthorizeAsync(Request, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"authorization_code":"abc"}""")]
    public async Task ThrowsWhenBankResponseIsMalformed(string json)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, json);

        await Assert.ThrowsAsync<AcquiringBankException>(() => CreateClient(handler).AuthorizeAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowsWhenBankIsUnreachable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));

        var exception = await Assert.ThrowsAsync<AcquiringBankException>(() => CreateClient(handler).AuthorizeAsync(Request, CancellationToken.None));
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"abc"}""");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler).AuthorizeAsync(Request, cancellation.Token));
    }
}