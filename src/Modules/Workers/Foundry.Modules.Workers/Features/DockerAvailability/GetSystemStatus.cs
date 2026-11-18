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
/// <param name="ExpiringTokenAccountCount">Valid accounts whose token expires within the warning window.</param>
/// <param name="ExpiredTokenAccountCount">Valid accounts whose token's expiry has passed.</param>
/// <param name="AccountTokenExpiryMessage">
/// The account-token-expiry banner copy, built by the server; <see langword="null"/> when no Valid account's
/// token is expiring soon or expired.
/// </param>
internal sealed record SystemStatus(
    bool DockerAvailable,
    int InvalidTokenAccountCount,
    string? AccountTokenMessage,
    int ExpiringTokenAccountCount,
    int ExpiredTokenAccountCount,
    string? AccountTokenExpiryMessage);

internal static class GetSystemStatus
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/status", static async (
                    IDockerAvailabilityState state,
                    IAccountTokenHealthQueries accountTokenHealth,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    int invalidTokenAccounts = await accountTokenHealth.CountInvalidAsync(cancellationToken);
                    AccountTokenExpiryCounts expiry =
                        await accountTokenHealth.CountExpiryAsync(timeProvider.GetUtcNow(), cancellationToken);

                    return TypedResults.Ok(new SystemStatus(
                        state.IsAvailable,
                        invalidTokenAccounts,
                        invalidTokenAccounts > 0 ? AccountTokenNotification.Message(invalidTokenAccounts) : null,
                        expiry.ExpiringSoon,
                        expiry.Expired,
                        expiry.Any ? AccountTokenExpiryNotification.Message(expiry.ExpiringSoon, expiry.Expired) : null));
                })
                .WithName("GetSystemStatus")
                .WithSummary("Returns system availability state: whether the Docker daemon is reachable and how many accounts have an invalid, expiring or expired token")
                .Produces<SystemStatus>();
        }
    }
}
