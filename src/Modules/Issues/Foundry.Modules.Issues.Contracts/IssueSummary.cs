namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// <paramref name="PublishPhase"/> is <c>publishing</c> or <c>retrying</c> while the issue's run publishes its commits
/// (ADR 0100), and <see langword="null"/> otherwise; it is set only for <c>in_progress</c> and
/// <c>revision_in_progress</c>.
/// <para>
/// <paramref name="Stage"/>, <paramref name="NeedsAction"/>, <paramref name="AwaitsDispatch"/> and
/// <paramref name="CodeStep"/> place the issue on the board (#851): the server decides, the dashboard renders.
/// <paramref name="Stage"/> is one of <see cref="TriageStage"/>, <see cref="ImplementStage"/>,
/// <see cref="AwaitingReviewStage"/> or <see cref="DoneStage"/>; <paramref name="CodeStep"/> is one of
/// <see cref="PublishingStep"/>, <see cref="PublishRetryingStep"/>, <see cref="RebasingStep"/> or
/// <see cref="ChecksStep"/>, or <see langword="null"/> when Foundry's own code runs no step for the issue.
/// </para>
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
    string Stage,
    bool NeedsAction,
    bool AwaitsDispatch,
    string? CodeStep,
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

    public const string TriageStage = "triage";
    public const string ImplementStage = "implement";
    public const string AwaitingReviewStage = "awaiting_review";
    public const string DoneStage = "done";

    public const string PublishingStep = "publishing";
    public const string PublishRetryingStep = "publish_retrying";
    public const string RebasingStep = "rebasing";
    public const string ChecksStep = "checks";
}
