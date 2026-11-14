using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.AccountTokenHealth;

/// <summary>
/// Tells the dashboard how many accounts have an invalid token whenever an account token becomes Invalid
/// or recovers. The count is read from committed state on every run, so a duplicated or reordered event
/// only re-sends the correct count. A pure SignalR broadcast: there is no durable consumer, so it does
/// not go through the outbox.
/// </summary>
internal sealed class AccountTokenBroadcastHandler(
    IAccountTokenHealthQueries queries,
    ISystemNotificationBroadcaster broadcaster,
    ILogger<AccountTokenBroadcastHandler> logger)
    : IDomainEventHandler<CredentialTokenInvalidated>, IDomainEventHandler<CredentialTokenRevalidated>
{
    public Task HandleAsync(CredentialTokenInvalidated @event, CancellationToken cancellationToken) =>
        BroadcastAsync(cancellationToken);

    public Task HandleAsync(CredentialTokenRevalidated @event, CancellationToken cancellationToken) =>
        BroadcastAsync(cancellationToken);

    // Both generic interfaces supply a default for the untyped member, so this handler picks one explicitly.
    Task IDomainEventHandler.HandleAsync(IDomainEvent @event, CancellationToken cancellationToken) =>
        @event switch
        {
            CredentialTokenInvalidated invalidated => HandleAsync(invalidated, cancellationToken),
            CredentialTokenRevalidated revalidated => HandleAsync(revalidated, cancellationToken),
            _ => throw new UnreachableException($"Unhandled domain event: {@event.GetType().Name}"),
        };

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        try
        {
            int count = await queries.CountInvalidAsync(cancellationToken);

            await broadcaster.SendAsync(
                new SystemNotification(
                    AccountTokenNotification.Category,
                    IsActive: count > 0,
                    Message: AccountTokenNotification.Message(count)),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The verdict or rotation is already committed and must not fail over a notification: the
            // dashboard re-seeds the banner from /api/system/status on load and on reconnect.
            logger.LogWarning(ex, "Could not broadcast the account token status.");
        }
    }
}
