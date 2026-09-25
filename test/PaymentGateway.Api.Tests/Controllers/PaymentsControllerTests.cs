using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Controllers;

public class PaymentsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

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
            Amount: 1050);
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
}