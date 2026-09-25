using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentGateway.Api.Configuration;
using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Support;

namespace PaymentGateway.Api.Tests.Services;

public class BankClientRegistrationTests
{
    private static readonly BankPaymentRequest Request = new()
    {
        CardNumber = "2222405343248870",
        ExpiryDate = "04/2027",
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    [Fact]
    public async Task DoesNotRetryPaymentWhenBankIsUnavailable()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.ServiceUnavailable, "{}");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHttpClient<IBankClient, BankClient>().ConfigurePrimaryHttpMessageHandler(() => handler)));
        var bankClient = factory.Services.GetRequiredService<IBankClient>();

        await Assert.ThrowsAsync<AcquiringBankException>(() => bankClient.AuthorizeAsync(Request, CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UsesConfiguredBankAddress()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"authorized":true,"authorization_code":"abc"}""");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Bank:BaseAddress", "http://configured-bank:9090")
            .ConfigureServices(services =>
                services.AddHttpClient<IBankClient, BankClient>().ConfigurePrimaryHttpMessageHandler(() => handler)));
        var bankClient = factory.Services.GetRequiredService<IBankClient>();

        await bankClient.AuthorizeAsync(Request, CancellationToken.None);

        Assert.Equal("http://configured-bank:9090/payments", Assert.Single(handler.Requests).Uri?.ToString());
    }

    [Fact]
    public void FailsOnStartupWhenBankAddressIsMissing()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Bank:BaseAddress", ""));

        var exception = Assert.Throws<AggregateException>(() => factory.CreateClient());

        Assert.All(exception.InnerExceptions, inner =>
        {
            var validationException = Assert.IsType<OptionsValidationException>(inner);
            Assert.Equal(typeof(BankOptions), validationException.OptionsType);
        });
    }
}