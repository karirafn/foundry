using System.Net;
using System.Text;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// A fake Package Source host: answers each absolute URL (query included) from its routes, and records every request
/// with the Authorization header it carried. An unrouted URL answers 404.
/// </summary>
internal sealed class FakeUpstream : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public List<(Uri Url, HttpMethod Method, string? Authorization)> Requests { get; } = [];

    public FakeUpstream Json(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
        Route(url, () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    public FakeUpstream Status(string url, HttpStatusCode status) =>
        Route(url, () => new HttpResponseMessage(status) { Content = new StringContent(string.Empty) });

    public FakeUpstream Redirect(string url, string location) =>
        Route(url, () =>
        {
            HttpResponseMessage response = new(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return response;
        });

    public FakeUpstream Throw(string url) =>
        Route(url, () => throw new HttpRequestException("Connection refused (feed.example:443)"));

    public static string ServiceIndex(string searchUrl) =>
        $$"""
        {
          "version": "3.0.0",
          "resources": [
            { "@id": "https://feed.example/packages/registration/", "@type": "RegistrationsBaseUrl" },
            { "@id": "{{searchUrl}}", "@type": "SearchQueryService/3.5.0" }
          ]
        }
        """;

    private FakeUpstream Route(string url, Func<HttpResponseMessage> respond)
    {
        _routes[new Uri(url).AbsoluteUri] = respond;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.RequestUri!, request.Method, request.Headers.Authorization?.ToString()));

        HttpResponseMessage response = _routes.TryGetValue(request.RequestUri!.AbsoluteUri, out Func<HttpResponseMessage>? respond)
            ? respond()
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) };

        return Task.FromResult(response);
    }
}
