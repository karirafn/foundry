using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Features.CredentialReactions;

/// <summary>
/// Clears the usage snapshot and triggers an immediate poll when credentials are validated.
/// Clearing fires unconditionally so another account's numbers never linger; the coordinator's
/// own gate ensures no container is started when <see cref="ClaudeAccount.CanReportUsage"/> is false.
/// </summary>
[IntegrationEventHandlerIdentity("Credentials.UsageCredentialsValidated")]
internal sealed class UsageCredentialsValidatedHandler(
    IUsageSnapshotStateMutator stateMutator,
    IUsagePollCoordinator coordinator) : IIntegrationEventHandler<CredentialsValidated>
{
    public async Task HandleAsync(CredentialsValidated @event, CancellationToken cancellationToken)
    {
        // Clear first so stale numbers from the previous account do not flash during the poll.
        stateMutator.Clear();
        await coordinator.PollAsync(cancellationToken);
    }
}
