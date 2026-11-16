using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Foundry.Shared.Infrastructure.Http.Ingress;

/// <summary>
/// The ingress port guard (ADR 0085) over the <see cref="IngressPortTable"/>. The decision uses the
/// connection's local port — never the spoofable <c>Host</c> header.
/// </summary>
public static class IngressPortGuard
{
    /// <summary>
    /// Adds an ingress entry, resolved once when the table is built. An entry without a port keeps its
    /// prefix refused on every port, for an ingress whose port is not configured.
    /// </summary>
    public static IServiceCollection AddIngressEntry(this IServiceCollection services, Func<IServiceProvider, IngressEntry> entry)
    {
        services.AddSingleton(new IngressEntryRegistration(entry));
        services.TryAddSingleton(static sp => new IngressPortTable(
            sp.GetServices<IngressEntryRegistration>()
                .Select(r => r.Resolve(sp))));
        return services;
    }

    /// <summary>Whether the request arrived on any ingress port.</summary>
    public static bool IsIngressPortRequest(HttpContext context) =>
        context.RequestServices.GetService<IngressPortTable>()?.IsIngressPort(context.Connection.LocalPort) ?? false;

    /// <summary>
    /// Responds 404 to any request the table does not allow. Register first in the pipeline. The table is
    /// built here, so a misconfigured table fails startup rather than the first request.
    /// </summary>
    public static IApplicationBuilder UseIngressPortGuard(this IApplicationBuilder app)
    {
        IngressPortTable? table = app.ApplicationServices.GetService<IngressPortTable>();
        if (table is null)
        {
            return app;
        }

        return app.Use(async (context, next) =>
        {
            if (!table.Allows(context.Connection.LocalPort, context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
    }

    private sealed record IngressEntryRegistration(Func<IServiceProvider, IngressEntry> Resolve);
}
