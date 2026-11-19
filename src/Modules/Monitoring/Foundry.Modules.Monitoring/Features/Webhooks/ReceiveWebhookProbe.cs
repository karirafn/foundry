using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Receives a webhook ingress self-probe (ADR 0116) on the webhook port: 204 for a code pending in this
/// process, which marks it arrived; 404 with no body for any other code, like an unknown hook id.
/// </summary>
internal static class ReceiveWebhookProbe
{
    internal static class Endpoint
    {
        public static void Map(IEndpointRouteBuilder routes)
        {
            routes.MapGet(WebhookIngressProbe.ProbePathPrefix + "{code}", static (string code, WebhookIngressProbe probe) =>
                    probe.TryRecordArrival(code) ? Results.NoContent() : Results.NotFound())
                .ExcludeFromDescription();
        }
    }
}
