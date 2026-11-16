using System.Net;
using System.Text;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.PackageSourceProxyTests;

/// <summary>
/// A fake Package Source host: answers each absolute URL (query included) from its routes and records every request
/// with the Authorization header it carried. An unrouted URL answers 404.
/// </summary>
internal sealed class FakePackageSourceUpstream : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public List<(Uri Url, HttpMethod Method, string? Authorization)> Requests { get; } = [];

    public FakePackageSourceUpstream Json(string url, string body, string mediaType = "application/json") =>
        Route(url, () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });

    public FakePackageSourceUpstream Bytes(string url, byte[] body) =>
        Route(url, () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream") },
            },
        });

    public FakePackageSourceUpstream Status(string url, HttpStatusCode status) =>
        Route(url, () => new HttpResponseMessage(status) { Content = new StringContent(string.Empty) });

    private FakePackageSourceUpstream Route(string url, Func<HttpResponseMessage> respond)
    {
        _routes[new Uri(url).AbsoluteUri] = respond;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add((request.RequestUri!, request.Method, request.Headers.Authorization?.ToString()));
        }

        if (_routes.TryGetValue(request.RequestUri!.AbsoluteUri, out Func<HttpResponseMessage>? respond))
        {
            return Task.FromResult(respond());
        }

        HttpResponseMessage response = new(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) };

        return Task.FromResult(response);
    }
}
