namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// <paramref name="PublishPhase"/> is <c>publishing</c> or <c>retrying</c> while the issue's run publishes its commits
/// (ADR 0100), and <see langword="null"/> otherwise; it is set only for <c>in_progress</c> and
/// <c>revision_in_progress</c>.
/// <para>
/// <paramref name="Stage"/>, <paramref name="NeedsAction"/>, <paramref name="AwaitsDispatch"/> and
/// <paramref name="CodeStep"/> place the issue on the board (#851): the server decides, the dashboard renders.
/// <paramref name="Stage"/> is one of <see cref="TriageStage"/>, <see cref="ImplementStage"/>, <see cref="ReviewStage"/>,
/// <see cref="AwaitingReviewStage"/> or <see cref="DoneStage"/>; <paramref name="CodeStep"/> is one of
/// <see cref="PublishingStep"/>, <see cref="PublishRetryingStep"/>, <see cref="RebasingStep"/>,
/// <see cref="ChecksStep"/>, <see cref="MergingStep"/> or <see cref="CatchUpWaitingStep"/>, or <see langword="null"/> when Foundry's own code runs no step for the issue.
/// </para>
/// <para>
/// <paramref name="RunKind"/> is the kind of worker run the issue runs next (#1059): one of <see cref="TriageRunKind"/>,
/// <see cref="FreshRunKind"/>, <see cref="ContinuationRunKind"/>, <see cref="ReviewFixRunKind"/>,
/// <see cref="ChecksFixRunKind"/>, <see cref="ConflictFixRunKind"/>, <see cref="InputResumeRunKind"/>,
/// <see cref="ReviewRunKind"/> or <see cref="FindingsFixRunKind"/>, or
/// <see langword="null"/> when the state has no run ahead.
/// </para>
/// <para>
/// <paramref name="RepositoryId"/> is the monitored repository's id: the same id as <c>RepositorySummary.Id</c> and the
/// repository settings route. <paramref name="WaitsOnBlockers"/> is true while the issue waits for its open blockers
/// to close, and <paramref name="EnteredStateAt"/> is when it entered its current state (#892).
/// </para>
/// <para>
/// <paramref name="MergeOutcome"/> is set for a <c>review</c> issue that left merge-pending without a merge:
/// <c>disabled</c> or <c>skipped</c>, with <paramref name="MergeSkippedReason"/> for <c>skipped</c> (ADR 0105).
/// <paramref name="ReviewOutcome"/> is set for a <c>review</c> issue whose review run failed open: <c>skipped</c>, with
/// <paramref name="ReviewSkippedReason"/> holding the run's failure category token (ADR 0113).
/// </para>
/// <para>
/// <paramref name="Round"/> is the issue's revision round, starting at 1: each revision trigger (review findings, a
/// person's feedback, failed checks, a rebase conflict) starts the next one.
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
    Guid RepositoryId,
    bool WaitsOnBlockers,
    DateTimeOffset EnteredStateAt,
    string? ChecksOutcome = null,
    string? PullRequestUrl = null,
    string? RebaseOutcome = null,
    string? RebaseSkippedReason = null,
    string? MergeOutcome = null,
    string? MergeSkippedReason = null,
    string? ProviderType = null,
    string? RevisionTriggerKind = null,
    string? PublishPhase = null,
    string? RunKind = null,
    string? ReviewOutcome = null,
    string? ReviewSkippedReason = null,
    int Round = 1)
{
    public const string PublishingPhase = "publishing";
    public const string RetryingPhase = "retrying";

    public const string TriageStage = "triage";
    public const string ImplementStage = "implement";
    public const string ReviewStage = "review";
    public const string AwaitingReviewStage = "awaiting_review";
    public const string DoneStage = "done";

    public const string TriageRunKind = "triage";
    public const string FreshRunKind = "fresh";
    public const string ContinuationRunKind = "continuation";
    public const string ReviewFixRunKind = "review_fix";
    public const string ChecksFixRunKind = "checks_fix";
    public const string ConflictFixRunKind = "conflict_fix";
    public const string InputResumeRunKind = "input_resume";
    public const string ReviewRunKind = "review";
    public const string FindingsFixRunKind = "findings_fix";

    public const string PublishingStep = "publishing";
    public const string PublishRetryingStep = "publish_retrying";
    public const string RebasingStep = "rebasing";
    public const string ChecksStep = "checks";

    /// <summary>Foundry is merging the pull request it opened, after its checks passed (ADR 0105).</summary>
    public const string MergingStep = "merging";

    /// <summary>
    /// The issue is merge-pending while its catch-up queue entry waits for its turn to rebase (ADR 0111): no merge runs.
    /// </summary>
    public const string CatchUpWaitingStep = "catch_up_waiting";
}
