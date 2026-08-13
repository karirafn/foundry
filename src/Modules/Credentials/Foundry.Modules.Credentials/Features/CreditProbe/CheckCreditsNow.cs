using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Credentials.Features.CreditProbe;

/// <summary>
/// POST /api/credentials/probe
/// Forces an immediate credit probe (single-flight).
/// Returns 202 Accepted with <c>inFlight: true</c> when a probe is already running,
/// so the client shows the in-flight state instead of starting another.
/// </summary>
internal static class CheckCreditsNow
{
    internal sealed record Response(bool InFlight, string? Outcome);

    internal static class Endpoint
    {
        internal static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/probe", static async (
                    ICreditProbeCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                await HandleAsync(coordinator, cancellationToken))
                .WithName("CheckCreditsNow")
                .WithSummary("Forces an immediate credit probe; returns 202 when already in flight")
                .Produces<Response>(StatusCodes.Status200OK)
                .Produces<Response>(StatusCodes.Status202Accepted)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }

        internal static async Task<IResult> HandleAsync(
            ICreditProbeCoordinator coordinator,
            CancellationToken cancellationToken)
        {
            CreditProbeResult result = await coordinator.TryRunProbeAsync(cancellationToken);

            if (result is CreditProbeResult.AlreadyRunning)
            {
                return TypedResults.Accepted((string?)null, new Response(InFlight: true, Outcome: null));
            }

            string outcome = result switch
            {
                CreditProbeResult.Restored => "restored",
                CreditProbeResult.StillBlocked => "stillBlocked",
                CreditProbeResult.UsageLimited => "usageLimited",
                CreditProbeResult.InfrastructureFailure => "infrastructureFailure",
                CreditProbeResult.Deferred => "deferred",
                CreditProbeResult.NoAccount => "noAccount",
                CreditProbeResult.NotBlocked => "notBlocked",
                _ => throw new System.Diagnostics.UnreachableException(
                    $"Unexpected CreditProbeResult: {result.GetType().Name}"),
            };
            return TypedResults.Ok(new Response(InFlight: false, Outcome: outcome));
        }
    }
}
