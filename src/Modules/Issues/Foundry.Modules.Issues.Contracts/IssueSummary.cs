namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// <paramref name="PublishPhase"/> is <c>publishing</c> or <c>retrying</c> while the issue's run publishes its commits
/// (ADR 0100), and <see langword="null"/> otherwise; it is set only for <c>in_progress</c> and
/// <c>revision_in_progress</c>.
/// </summary>
public sealed record IssueSummary(
    Guid Id,
    int IssueNumber,
    string Title,
    string State,
    string RepositorySlug,
    DateTimeOffset DetectedAt,
    string Url,
    string? FailureClassification,
    string? RepositoryEligibilityStatus,
    RunStats? RunStats,
    string? ChecksOutcome = null,
    string? PullRequestUrl = null,
    string? RebaseOutcome = null,
    string? RebaseSkippedReason = null,
    string? ProviderType = null,
    string? RevisionTriggerKind = null,
    string? PublishPhase = null)
{
    public const string PublishingPhase = "publishing";
    public const string RetryingPhase = "retrying";
}
