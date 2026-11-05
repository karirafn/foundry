using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// Raised when an issue enters the rebase-pending state (ADR 0087). Monitoring nudges its rebase step from
/// it, so the rebase runs straight away rather than on the next scheduled poll.
/// </summary>
public sealed record IssueEnteredRebasePending(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    DateTimeOffset EnteredRebasePendingAt) : IIntegrationEvent;
