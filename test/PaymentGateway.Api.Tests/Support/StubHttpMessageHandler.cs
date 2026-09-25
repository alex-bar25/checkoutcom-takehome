using System.Net;

namespace PaymentGateway.Api.Tests.Support;

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<CapturedRequest> Requests { get; } = [];

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string? json = null) =>
        new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json ?? string.Empty, System.Text.Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));

        return _respond(request);
    }

    public record CapturedRequest(HttpMethod Method, Uri? Uri, string? Body);
}