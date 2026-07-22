using System.Net;
using System.Text;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

internal sealed class FakeHandler(HttpStatusCode statusCode, string responseBody) : DelegatingHandler
{
    private readonly List<(string PathContains, HttpStatusCode StatusCode, string Body)> _routes = [];
    private readonly List<HttpRequestMessage> _allRequests = [];

    public HttpRequestMessage? LastRequest { get; private set; }
    public IReadOnlyList<HttpRequestMessage> AllRequests => _allRequests;
    public Dictionary<string, string> ResponseHeaders { get; } = new();

    public FakeHandler WithRoute(string pathContains, HttpStatusCode routeStatusCode, string body)
    {
        _routes.Add((pathContains, routeStatusCode, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        _allRequests.Add(request);

        string path = request.RequestUri?.AbsolutePath ?? string.Empty;

        (HttpStatusCode resolvedStatus, string resolvedBody) = ResolveResponse(path);

        HttpResponseMessage response = new(resolvedStatus)
        {
            Content = new StringContent(resolvedBody, Encoding.UTF8, "application/json"),
        };

        foreach (KeyValuePair<string, string> header in ResponseHeaders)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return Task.FromResult(response);
    }

    private (HttpStatusCode Status, string Body) ResolveResponse(string path)
    {
        foreach ((string pathContains, HttpStatusCode routeStatus, string routeBody) in _routes)
        {
            if (path.Contains(pathContains, StringComparison.Ordinal))
            {
                return (routeStatus, routeBody);
            }
        }

        return (statusCode, responseBody);
    }
}
