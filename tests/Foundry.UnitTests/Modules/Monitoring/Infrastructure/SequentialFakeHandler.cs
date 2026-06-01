using System.Net;
using System.Text;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

/// <summary>
/// A delegating handler that returns responses in sequence — one per request.
/// Useful when the system under test makes multiple HTTP calls in order.
/// </summary>
internal sealed class SequentialFakeHandler : DelegatingHandler
{
    private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses;

    public SequentialFakeHandler(IEnumerable<(HttpStatusCode StatusCode, string Body)> responses)
    {
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        (HttpStatusCode statusCode, string body) = _responses.Dequeue();

        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        return Task.FromResult(response);
    }
}
