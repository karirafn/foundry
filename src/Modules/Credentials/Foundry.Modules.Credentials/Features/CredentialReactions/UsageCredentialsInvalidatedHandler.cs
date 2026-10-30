using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Features.CredentialReactions;

/// <summary>
/// Clears the usage snapshot and broadcasts null when credentials are invalidated.
/// No re-poll is triggered: the coordinator would gate on <see cref="ClaudeAccount.CanReportUsage"/>
/// (false for an invalid account) and return immediately — clearing and broadcasting null directly
/// is cleaner and avoids an unnecessary coordinator call.
/// </summary>
[IntegrationEventHandlerIdentity("Credentials.UsageCredentialsInvalidated")]
internal sealed class UsageCredentialsInvalidatedHandler(
    IUsageSnapshotStateMutator stateMutator,
    IUsageSnapshotBroadcaster broadcaster) : IIntegrationEventHandler<CredentialsInvalidated>
{
    public async Task HandleAsync(CredentialsInvalidated @event, CancellationToken cancellationToken)
    {
        stateMutator.Clear();
        await broadcaster.SendSnapshotAsync(null, cancellationToken);
    }
}
