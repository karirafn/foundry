using System.Net;
using System.Text;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

/// <summary>
/// A delegating handler that tracks how many HTTP requests were issued.
/// Useful for verifying that caching suppresses redundant HTTP calls.
/// </summary>
internal sealed class CountingFakeHandler(HttpStatusCode statusCode, string responseBody) : DelegatingHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;

        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };

        return Task.FromResult(response);
    }
}
