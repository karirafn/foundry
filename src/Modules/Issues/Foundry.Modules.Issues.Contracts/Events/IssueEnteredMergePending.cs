using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// Raised when an issue enters the merge-pending state (ADR 0105). Monitoring nudges its merge step from it, so
/// the merge runs straight away rather than on the next scheduled poll.
/// </summary>
public sealed record IssueEnteredMergePending(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    DateTimeOffset EnteredMergePendingAt) : IIntegrationEvent;
