using System.Net.Http.Json;
using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PaymentGateway.Api.EndToEndTests;

public sealed class BankSimulatorFixture : IAsyncLifetime
{
    private const int BankPort = 8080;
    private const int AdminPort = 2525;

    private readonly IContainer _bankSimulator = new ContainerBuilder("bbyars/mountebank:2.8.1")
        .WithBindMount(Path.Combine(CommonDirectoryPath.GetSolutionDirectory().DirectoryPath, "imposters"), "/imposters", AccessMode.ReadOnly)
        .WithCommand("--configfile", "/imposters/bank_simulator.ejs", "--allowInjection")
        .WithPortBinding(BankPort, assignRandomHostPort: true)
        .WithPortBinding(AdminPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(AdminPort).ForPath($"/imposters/{BankPort}")))
        .Build();

    private WebApplicationFactory<Program>? _gateway;
    private HttpClient? _bankSimulatorAdmin;

    public HttpClient Gateway { get; private set; } = null!;

    public async Task<int> GetBankRequestCountAsync()
    {
        var imposter = await _bankSimulatorAdmin!.GetFromJsonAsync<JsonElement>($"/imposters/{BankPort}");
        return imposter.GetProperty("numberOfRequests").GetInt32();
    }

    public async Task InitializeAsync()
    {
        await _bankSimulator.StartAsync();

        _bankSimulatorAdmin = new HttpClient { BaseAddress = SimulatorUri(AdminPort) };
        _gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Bank:BaseAddress", SimulatorUri(BankPort).ToString()));
        Gateway = _gateway.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Gateway.Dispose();
        _bankSimulatorAdmin?.Dispose();

        if (_gateway is not null)
        {
            await _gateway.DisposeAsync();
        }

        await _bankSimulator.DisposeAsync();
    }

    private Uri SimulatorUri(int port) => new($"http://{_bankSimulator.Hostname}:{_bankSimulator.GetMappedPublicPort(port)}");
}