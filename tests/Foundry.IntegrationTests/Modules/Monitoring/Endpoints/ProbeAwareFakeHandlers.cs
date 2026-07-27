using System.Net;
using System.Text;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints;

/// <summary>
/// Returns 422 for all probe POSTs (Granted) and a minimal empty listing for GET requests,
/// so the write-permission probe passes and branch-protection evaluation proceeds.
/// </summary>
internal sealed class ProbeGrantedFakeHandler : DelegatingHandler
{
    private const string EmptyListingJson = "[]";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (StaticListingFakeHandler.IsProbePost(request))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
        }

        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(EmptyListingJson, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

/// <summary>
/// Returns 403 for all probe POSTs so the eligibility evaluator classifies the repository
/// as Ineligible with a cannot-push violation.
/// </summary>
internal sealed class ProbeBlockedFakeHandler : DelegatingHandler
{
    private const string EmptyListingJson = "[]";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (StaticListingFakeHandler.IsProbePost(request))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }

        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(EmptyListingJson, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
