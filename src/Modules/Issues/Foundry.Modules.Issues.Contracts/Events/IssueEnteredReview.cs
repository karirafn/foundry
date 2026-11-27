using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// Raised when an issue enters review. Monitoring nudges its review pass from it, so the draft pull request is marked
/// ready straight away rather than on the next scheduled poll (ADR 0120).
/// </summary>
public sealed record IssueEnteredReview(MonitoredRepositoryId RepositoryId, int IssueNumber) : IIntegrationEvent;
