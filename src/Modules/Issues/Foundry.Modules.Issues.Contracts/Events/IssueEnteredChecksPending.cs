using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// Raised when an issue enters the checks-pending state. Monitoring schedules checks-pass nudges from
/// it — one immediately and not-before nudges at the grace-window and upper-bound deadlines — so the
/// checks gate resolves on time even while the repository polls at the Backstop Interval (ADR 0085).
/// </summary>
public sealed record IssueEnteredChecksPending(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    DateTimeOffset EnteredChecksPendingAt) : IIntegrationEvent;
