using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// POST /api/settings/oauth/login/start
/// Starts a new OAuth login session (or reuses the active one) and returns 202 Accepted
/// immediately. The container start, URL scan, and timeouts run in the background;
/// the URL and result are delivered later via SignalR.
/// </summary>
internal static class StartLogin
{
    internal sealed record Response(Guid SessionId);

    internal static class Endpoint
    {
        internal static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/start", static async (
                    LoginSessionService service,
                    CancellationToken cancellationToken) =>
                await HandleAsync(service, cancellationToken))
                .WithName("StartOAuthLogin")
                .WithSummary("Starts an OAuth login session (non-blocking; result delivered via SignalR)")
                .Produces<Response>(StatusCodes.Status202Accepted);
        }

        internal static async Task<Accepted<Response>> HandleAsync(
            LoginSessionService service,
            CancellationToken cancellationToken)
        {
            Guid sessionId = await service.StartAsync(cancellationToken);
            return TypedResults.Accepted((string?)null, new Response(sessionId));
        }
    }
}
