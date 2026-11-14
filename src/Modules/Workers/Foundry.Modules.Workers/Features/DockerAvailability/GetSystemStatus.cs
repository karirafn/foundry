using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features.DockerAvailability;

/// <param name="AccountTokenMessage">
/// The account-token banner copy, built by the server; <see langword="null"/> when no account has an
/// invalid token.
/// </param>
internal sealed record SystemStatus(bool DockerAvailable, int InvalidTokenAccountCount, string? AccountTokenMessage);

internal static class GetSystemStatus
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/status", static async (
                    IDockerAvailabilityState state,
                    IAccountTokenHealthQueries accountTokenHealth,
                    CancellationToken cancellationToken) =>
                {
                    int invalidTokenAccounts = await accountTokenHealth.CountInvalidAsync(cancellationToken);

                    return TypedResults.Ok(new SystemStatus(
                        state.IsAvailable,
                        invalidTokenAccounts,
                        invalidTokenAccounts > 0 ? AccountTokenNotification.Message(invalidTokenAccounts) : null));
                })
                .WithName("GetSystemStatus")
                .WithSummary("Returns system availability state: whether the Docker daemon is reachable and how many accounts have an invalid token")
                .Produces<SystemStatus>();
        }
    }
}
