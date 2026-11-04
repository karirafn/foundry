using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A verified provider push delivery (GitHub <c>push</c>, GitLab Push Hook) touched the branch of a
/// running worker. Ephemeral: delivered directly via <c>IIntegrationEventProcessor</c>, never through
/// the outbox — a lost push only leaves the display-only commit count stale until the next push or the
/// container's exit.
/// </summary>
/// <param name="RepositoryId">The monitored repository the push was delivered for.</param>
/// <param name="Branch">The pushed branch name, without the <c>refs/heads/</c> prefix.</param>
public sealed record ProviderBranchPushed(
    MonitoredRepositoryId RepositoryId,
    string Branch) : IIntegrationEvent;
