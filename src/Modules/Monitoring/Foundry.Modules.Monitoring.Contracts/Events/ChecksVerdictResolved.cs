using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The checks gate resolved for an issue. <paramref name="HeadSha"/> is the pull request's head commit as read by
/// the status read in the same poll, for every outcome. It is nullable only so outbox rows written before it
/// existed still deserialize; a null stores no Verified Head, and the review pass re-checks the issue once.
/// <paramref name="AutoMerge"/> is the repository's live Merge When Checks Pass setting when the verdict was
/// recorded (ADR 0105); rows written before it existed deserialize as <see langword="false"/> and route to review.
/// <paramref name="ReviewPullRequests"/> is the repository's live Review Pull Requests setting when the verdict was
/// recorded (ADR 0113); rows written before it existed deserialize as <see langword="false"/> and skip the review step.
/// </summary>
public sealed record ChecksVerdictResolved(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string Outcome,
    IReadOnlyList<FailingCheck> FailingChecks,
    string? HeadSha,
    bool AutoMerge = false,
    bool ReviewPullRequests = false) : IIntegrationEvent;
