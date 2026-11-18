using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.AccountTokenHealth;

/// <summary>
/// Counts the Valid accounts whose token is expiring soon or expired and sends the account-token-expiry
/// banner. A pure SignalR broadcast: there is no durable consumer, so it does not go through the outbox.
/// </summary>
internal sealed class AccountTokenExpiryBroadcaster(
    IAccountTokenHealthQueries queries,
    ISystemNotificationBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<AccountTokenExpiryBroadcaster> logger)
{
    /// <summary>Reads the current counts.</summary>
    public Task<AccountTokenExpiryCounts> CountAsync(CancellationToken cancellationToken) =>
        queries.CountExpiryAsync(timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>Reads the current counts and broadcasts them.</summary>
    public async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        try
        {
            AccountTokenExpiryCounts counts = await CountAsync(cancellationToken);
            await SendAsync(counts, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(ex);
        }
    }

    /// <summary>
    /// Broadcasts <paramref name="counts"/>. Returns whether the broadcast was sent; a failure is logged and
    /// swallowed, because the dashboard re-seeds the banner from /api/system/status on load and on reconnect.
    /// </summary>
    public async Task<bool> SendAsync(AccountTokenExpiryCounts counts, CancellationToken cancellationToken)
    {
        try
        {
            await broadcaster.SendAsync(
                new SystemNotification(
                    AccountTokenExpiryNotification.Category,
                    IsActive: counts.Any,
                    Message: AccountTokenExpiryNotification.Message(counts.ExpiringSoon, counts.Expired)),
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(ex);
            return false;
        }
    }

    private void LogFailure(Exception ex) =>
        logger.LogWarning(ex, "Could not broadcast the account token expiry status.");
}
