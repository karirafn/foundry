using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// The two-way port guard (ADR 0085). The WebApi is unauthenticated, so the public webhook port must
/// expose nothing but <c>/webhooks/*</c>, and the main port must never serve <c>/webhooks/*</c>.
/// The decision uses the connection's local port — never the spoofable <c>Host</c> header.
/// </summary>
public static class WebhookPortGuard
{
    internal const string WebhooksPathPrefix = "/webhooks";

    /// <summary>Whether the request arrived on the configured webhook port.</summary>
    public static bool IsWebhookPortRequest(HttpContext context)
    {
        int? port = context.RequestServices.GetRequiredService<IOptions<WebhookIngressOptions>>().Value.Port;
        return port is { } webhookPort && context.Connection.LocalPort == webhookPort;
    }

    /// <summary>
    /// Responds 404 to any request outside <c>/webhooks/*</c> on the webhook port, and to any
    /// <c>/webhooks/*</c> request on another port. Register first in the pipeline.
    /// </summary>
    public static IApplicationBuilder UseWebhookPortGuard(this IApplicationBuilder app)
    {
        return app.Use(static async (context, next) =>
        {
            bool isWebhookPath = context.Request.Path.StartsWithSegments(WebhooksPathPrefix, StringComparison.OrdinalIgnoreCase);

            if (IsWebhookPortRequest(context) != isWebhookPath)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
    }
}
