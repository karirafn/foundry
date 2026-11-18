using System.Diagnostics;

using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.AccountTokenHealth;

/// <summary>
/// Recounts and broadcasts the account-token-expiry banner whenever an account change can move it: a token
/// becoming Invalid or Valid, a token being set, or an account being deleted. The counts are read from
/// committed state on every run, so a duplicated or reordered event only re-sends the correct counts.
/// </summary>
internal sealed class AccountTokenExpiryBroadcastHandler(AccountTokenExpiryBroadcaster broadcaster)
    : IDomainEventHandler<CredentialTokenInvalidated>,
        IDomainEventHandler<CredentialTokenRevalidated>,
        IDomainEventHandler<CredentialTokenSet>,
        IDomainEventHandler<CredentialDeleted>
{
    public Task HandleAsync(CredentialTokenInvalidated @event, CancellationToken cancellationToken) =>
        broadcaster.BroadcastAsync(cancellationToken);

    public Task HandleAsync(CredentialTokenRevalidated @event, CancellationToken cancellationToken) =>
        broadcaster.BroadcastAsync(cancellationToken);

    public Task HandleAsync(CredentialTokenSet @event, CancellationToken cancellationToken) =>
        broadcaster.BroadcastAsync(cancellationToken);

    public Task HandleAsync(CredentialDeleted @event, CancellationToken cancellationToken) =>
        broadcaster.BroadcastAsync(cancellationToken);

    // Every generic interface supplies a default for the untyped member, so this handler picks one explicitly.
    Task IDomainEventHandler.HandleAsync(IDomainEvent @event, CancellationToken cancellationToken) =>
        @event switch
        {
            CredentialTokenInvalidated invalidated => HandleAsync(invalidated, cancellationToken),
            CredentialTokenRevalidated revalidated => HandleAsync(revalidated, cancellationToken),
            CredentialTokenSet set => HandleAsync(set, cancellationToken),
            CredentialDeleted deleted => HandleAsync(deleted, cancellationToken),
            _ => throw new UnreachableException($"Unhandled domain event: {@event.GetType().Name}"),
        };
}
