using System.Net;
using System.Text;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

internal sealed record RecordedRequest(HttpMethod Method, string Path, string? Body, string? ApiVersion, string? Token);

/// <summary>
/// A terminal handler that answers by HTTP method and exact path, and records each request with its body
/// and GitHub headers read before the request is disposed. <see cref="FakeHandler"/> routes by path alone, so a
/// GET and a POST to one path cannot get different answers there.
/// </summary>
internal sealed class MethodRoutedFakeHandler : HttpMessageHandler
{
    private const string ApiVersionHeader = "X-GitHub-Api-Version";

    private readonly List<(HttpMethod Method, string Path, HttpStatusCode Status, string Body)> _responses = [];
    private readonly List<(HttpMethod Method, string Path)> _failures = [];
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public Dictionary<string, string> ResponseHeaders { get; } = [];

    public MethodRoutedFakeHandler FailToConnect(HttpMethod method, string path)
    {
        _failures.Add((method, path));
        return this;
    }

    public MethodRoutedFakeHandler Respond(HttpMethod method, string path, HttpStatusCode status, string body)
    {
        _responses.Add((method, path, status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        string? apiVersion = request.Headers.TryGetValues(ApiVersionHeader, out IEnumerable<string>? values)
            ? values.Single()
            : null;
        _requests.Add(new RecordedRequest(request.Method, path, body, apiVersion, request.Headers.Authorization?.Parameter));

        if (_failures.Any(failure => failure.Method == request.Method && failure.Path == path))
        {
            throw new HttpRequestException("connection refused");
        }

        (HttpMethod Method, string Path, HttpStatusCode Status, string Body) match = _responses
            .First(response => response.Method == request.Method && response.Path == path);
        HttpResponseMessage response = new(match.Status)
        {
            Content = new StringContent(match.Body, Encoding.UTF8, "application/json"),
        };

        foreach (KeyValuePair<string, string> header in ResponseHeaders)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return response;
    }
}
