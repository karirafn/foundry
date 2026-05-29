using System.Net;
using System.Text;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

internal sealed class FakeHandler(HttpStatusCode statusCode, string responseBody) : DelegatingHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public Dictionary<string, string> ResponseHeaders { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;

        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };

        foreach (KeyValuePair<string, string> header in ResponseHeaders)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return Task.FromResult(response);
    }
}
