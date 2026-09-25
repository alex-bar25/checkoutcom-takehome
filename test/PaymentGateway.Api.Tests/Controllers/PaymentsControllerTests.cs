using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Controllers;

public class PaymentsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CardNumber = "2222405343248877";
    private const string Cvv = "9876";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly IBankClient _bankClient = Substitute.For<IBankClient>();

    public PaymentsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RetrievesAPaymentSuccessfully()
    {
        // Arrange
        var payment = new Payment(
            Id: Guid.NewGuid(),
            Status: PaymentStatus.Authorized,
            CardNumberLastFour: "0123",
            ExpiryMonth: 4,
            ExpiryYear: 2030,
            Currency: "GBP",
            Amount: 1050,
            AuthorizationCode: "auth-123");
        _factory.Services.GetRequiredService<IPaymentsRepository>().Add(payment);
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/payments/{payment.Id}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payment.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Authorized", body.GetProperty("status").GetString());
        Assert.Equal("0123", body.GetProperty("cardNumberLastFour").GetString());
        Assert.Equal(4, body.GetProperty("expiryMonth").GetInt32());
        Assert.Equal(2030, body.GetProperty("expiryYear").GetInt32());
        Assert.Equal("GBP", body.GetProperty("currency").GetString());
        Assert.Equal(1050, body.GetProperty("amount").GetInt32());
        Assert.False(body.TryGetProperty("authorizationCode", out _));
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/payments/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProcessesAuthorizedPaymentAndMakesItRetrievable()
    {
        // Arrange
        BankResponds(authorized: true);
        var client = CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/payments", ValidRequest());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Authorized", body.GetProperty("status").GetString());
        Assert.Equal("8877", body.GetProperty("cardNumberLastFour").GetString());
        Assert.Equal(4, body.GetProperty("expiryMonth").GetInt32());
        Assert.Equal(NextYear, body.GetProperty("expiryYear").GetInt32());
        Assert.Equal("GBP", body.GetProperty("currency").GetString());
        Assert.Equal(1050, body.GetProperty("amount").GetInt32());

        var retrieved = await client.GetFromJsonAsync<JsonElement>(response.Headers.Location);
        Assert.Equal(body.GetProperty("id").GetGuid(), retrieved.GetProperty("id").GetGuid());
        Assert.Equal("Authorized", retrieved.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ProcessesDeclinedPayment()
    {
        // Arrange
        BankResponds(authorized: false);
        var client = CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/payments", ValidRequest());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Declined", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RejectsInvalidPaymentWithoutCallingBank()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/payments", ValidRequest(cardNumber: "1234", currency: "JPY"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        var errors = body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("cardNumber", out _));
        Assert.True(errors.TryGetProperty("currency", out _));
        await _bankClient.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default);
    }

    [Theory]
    [InlineData("""{"cardNumber":"2222405343248877","expiryMonth":4,"expiryYear":2099,"currency":"GBP","amount":10.5,"cvv":"123"}""", "amount")]
    [InlineData("""{"cardNumber":"2222405343248877","expiryMonth":"April","expiryYear":2099,"currency":"GBP","amount":1050,"cvv":"123"}""", "expiryMonth")]
    [InlineData("""{"cardNumber":2222405343248877,"expiryMonth":4,"expiryYear":2099,"currency":"GBP","amount":1050,"cvv":"123"}""", "cardNumber")]
    [InlineData("", "request")]
    [InlineData("not json", "request")]
    public async Task RejectsMalformedPaymentWithoutCallingBank(string json, string expectedErrorField)
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.PostAsync("/api/payments", new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        var errorField = Assert.Single(body.GetProperty("errors").EnumerateObject());
        Assert.Equal(expectedErrorField, errorField.Name);
        await _bankClient.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.BadGateway)]
    [InlineData(true, HttpStatusCode.GatewayTimeout)]
    public async Task ReturnsGatewayErrorWhenBankFails(bool outcomeUnknown, HttpStatusCode expectedStatusCode)
    {
        // Arrange
        _bankClient.AuthorizeAsync(default!, default)
            .ThrowsAsyncForAnyArgs(new AcquiringBankException("Acquiring bank failed.", outcomeUnknown));
        var client = CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/payments", ValidRequest());

        // Assert
        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task NeverReturnsFullCardNumberOrCvv()
    {
        // Arrange
        BankResponds(authorized: true);
        var client = CreateClient();

        // Act
        var processed = await client.PostAsJsonAsync("/api/payments", ValidRequest());
        var rejected = await client.PostAsJsonAsync("/api/payments", ValidRequest(currency: "JPY"));
        var retrieved = await client.GetAsync(processed.Headers.Location);

        // Assert
        foreach (var response in new[] { processed, rejected, retrieved })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(CardNumber, body);
            Assert.DoesNotContain("cvv", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task NeverLogsFullCardNumberOrCvv()
    {
        // Arrange
        BankResponds(authorized: true);
        using var factory = _factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddFakeLogging())
            .ConfigureServices(services => services.AddSingleton(_bankClient)));
        var client = factory.CreateClient();

        // Act
        await client.PostAsJsonAsync("/api/payments", ValidRequest());
        await client.PostAsJsonAsync("/api/payments", ValidRequest(currency: "JPY"));

        // Assert
        var records = factory.Services.GetFakeLogCollector().GetSnapshot();
        Assert.NotEmpty(records);
        Assert.All(records, record =>
        {
            Assert.DoesNotContain(CardNumber, record.Message);
            Assert.DoesNotContain(record.StructuredState ?? [], state => state.Value is CardNumber or Cvv);
        });
    }

    [Fact]
    public async Task PreventsCachingOfPaymentResponses()
    {
        // Arrange
        BankResponds(authorized: true);
        var client = CreateClient();

        // Act
        var processed = await client.PostAsJsonAsync("/api/payments", ValidRequest());
        var retrieved = await client.GetAsync(processed.Headers.Location);

        // Assert
        Assert.True(processed.Headers.CacheControl?.NoStore);
        Assert.True(retrieved.Headers.CacheControl?.NoStore);
    }

    private static int NextYear => DateTime.UtcNow.Year + 1;

    private static object ValidRequest(string cardNumber = CardNumber, string currency = "GBP") => new
    {
        cardNumber,
        expiryMonth = 4,
        expiryYear = NextYear,
        currency,
        amount = 1050,
        cvv = Cvv
    };

    private void BankResponds(bool authorized) =>
        _bankClient.AuthorizeAsync(default!, default).ReturnsForAnyArgs(new BankPaymentResponse
        {
            Authorized = authorized,
            AuthorizationCode = authorized ? "0bb07405-6d44-4b50-a14f-7ae0beff13ad" : ""
        });

    private HttpClient CreateClient() =>
        _factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton(_bankClient)))
            .CreateClient();
}