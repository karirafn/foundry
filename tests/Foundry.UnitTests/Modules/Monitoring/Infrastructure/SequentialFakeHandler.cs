using System.Net;
using System.Text;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

/// <summary>
/// A delegating handler that returns responses in sequence — one per request.
/// Useful when the system under test makes multiple HTTP calls in order.
/// </summary>
internal sealed class SequentialFakeHandler : DelegatingHandler
{
    private readonly Queue<(HttpStatusCode StatusCode, string Body, IReadOnlyDictionary<string, string> Headers)> _responses;

    public SequentialFakeHandler(IEnumerable<(HttpStatusCode StatusCode, string Body)> responses)
    {
        _responses = new Queue<(HttpStatusCode, string, IReadOnlyDictionary<string, string>)>(
            responses.Select(r => (r.StatusCode, r.Body, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>())));
    }

    public SequentialFakeHandler(
        IEnumerable<(HttpStatusCode StatusCode, string Body, IReadOnlyDictionary<string, string> Headers)> responses)
    {
        _responses = new Queue<(HttpStatusCode, string, IReadOnlyDictionary<string, string>)>(responses);
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        (HttpStatusCode statusCode, string body, IReadOnlyDictionary<string, string> headers) = _responses.Dequeue();

        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        foreach (KeyValuePair<string, string> header in headers)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return Task.FromResult(response);
    }
}
