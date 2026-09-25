using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PaymentGateway.Api.EndToEndTests;

public class PaymentsEndToEndTests : IClassFixture<BankSimulatorFixture>
{
    private readonly BankSimulatorFixture _fixture;

    public PaymentsEndToEndTests(BankSimulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("1", "Authorized")]
    [InlineData("3", "Authorized")]
    [InlineData("5", "Authorized")]
    [InlineData("7", "Authorized")]
    [InlineData("9", "Authorized")]
    [InlineData("2", "Declined")]
    [InlineData("4", "Declined")]
    [InlineData("6", "Declined")]
    [InlineData("8", "Declined")]
    public async Task ProcessesPaymentAccordingToBankDecision(string lastCardDigit, string expectedStatus)
    {
        var response = await _fixture.Gateway.PostAsJsonAsync("/api/payments", Payment($"222240534324887{lastCardDigit}"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expectedStatus, body.GetProperty("status").GetString());
        Assert.Equal($"887{lastCardDigit}", body.GetProperty("cardNumberLastFour").GetString());
    }

    [Fact]
    public async Task RetrievesProcessedPayment()
    {
        var created = await _fixture.Gateway.PostAsJsonAsync("/api/payments", Payment("2222405343248877"));
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();

        var retrieved = await _fixture.Gateway.GetFromJsonAsync<JsonElement>(created.Headers.Location);

        Assert.Equal(createdBody.GetProperty("id").GetGuid(), retrieved.GetProperty("id").GetGuid());
        Assert.Equal("Authorized", retrieved.GetProperty("status").GetString());
        Assert.Equal("8877", retrieved.GetProperty("cardNumberLastFour").GetString());
        Assert.Equal(1050, retrieved.GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task ReturnsBadGatewayWhenBankIsUnavailable()
    {
        var response = await _fixture.Gateway.PostAsJsonAsync("/api/payments", Payment("2222405343248870"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task RejectsInvalidPaymentWithoutCallingBank()
    {
        var bankRequestsBefore = await _fixture.GetBankRequestCountAsync();

        var response = await _fixture.Gateway.PostAsJsonAsync("/api/payments", Payment("2222405343248877", currency: "JPY"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(bankRequestsBefore, await _fixture.GetBankRequestCountAsync());
    }

    private static object Payment(string cardNumber, string currency = "GBP") => new
    {
        cardNumber,
        expiryMonth = 4,
        expiryYear = DateTime.UtcNow.Year + 1,
        currency,
        amount = 1050,
        cvv = "123"
    };
}