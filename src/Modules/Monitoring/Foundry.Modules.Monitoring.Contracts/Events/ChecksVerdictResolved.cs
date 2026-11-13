using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The checks gate resolved for an issue. <paramref name="HeadSha"/> is the pull request's head commit as read by
/// the status read in the same poll, for every outcome. It is nullable only so outbox rows written before it
/// existed still deserialize; a null stores no Verified Head, and the review pass re-checks the issue once.
/// </summary>
public sealed record ChecksVerdictResolved(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string Outcome,
    IReadOnlyList<FailingCheck> FailingChecks,
    string? HeadSha) : IIntegrationEvent;
