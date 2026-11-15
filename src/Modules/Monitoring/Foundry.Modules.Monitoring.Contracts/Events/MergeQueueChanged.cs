using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The merge step changed what the catch-up queue (ADR 0111) shows for <paramref name="IssueNumbers"/> — a waiting
/// entry's position or awaited issue, or an entry's rounds — without changing their state. Ephemeral: delivered
/// directly via <c>IIntegrationEventProcessor</c>, never through the outbox — a lost one only leaves an open issue
/// pane stale until the next change.
/// </summary>
/// <param name="RepositoryId">The monitored repository whose queue changed.</param>
/// <param name="IssueNumbers">The issues whose queue entry changed, in ascending order.</param>
public sealed record MergeQueueChanged(
    MonitoredRepositoryId RepositoryId,
    IReadOnlyList<int> IssueNumbers) : IIntegrationEvent;
