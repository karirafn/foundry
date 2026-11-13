using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public sealed record IssueDetail(
    Guid Id,
    int IssueNumber,
    string Title,
    string State,
    string RepositorySlug,
    DateTimeOffset DetectedAt,
    string Url,
    string Author,
    IReadOnlyList<string> Labels,
    IssueStateDetails? StateDetails,
    IssueKindDetails Kind,
    TriageVerdictDetails? Triage);

/// <summary>
/// How the issue's kind was decided (ADR 0097): <paramref name="Source"/> is <c>labelled</c>, <c>triaged</c> or
/// <c>unclassified</c>, and <paramref name="Kind"/> is <see langword="null"/> only when unclassified.
/// <paramref name="ConflictingTriageKind"/> is set only when a label decided and triage judged a different kind;
/// nothing acts on it.
/// </summary>
public sealed record IssueKindDetails(string Source, string? Kind, string? ConflictingTriageKind)
{
    public const string LabelledSource = "labelled";
    public const string TriagedSource = "triaged";
    public const string UnclassifiedSource = "unclassified";
}

/// <summary>
/// The issue's latest triage verdict: <paramref name="Outcome"/> is <c>ready</c>, <c>needs_input</c> or <c>blocked</c>.
/// </summary>
public sealed record TriageVerdictDetails(
    string Outcome,
    string Kind,
    IReadOnlyList<string> Questions,
    DateTimeOffset DecidedAt)
{
    public const string ReadyOutcome = "ready";
    public const string NeedsInputOutcome = "needs_input";
    public const string BlockedOutcome = "blocked";
}

/// <summary>
/// The current state's fields. <paramref name="BranchUrl"/>, <paramref name="SideBranchName"/>,
/// <paramref name="SideBranchUrl"/> and <paramref name="DivergenceReason"/> are set only for <c>branch_diverged</c>: the
/// issue branch and the side branch the run's work was pushed to, with their web URLs on the provider, and why
/// (<c>branch-moved</c> or <c>force-push-protected</c>).
/// </summary>
public sealed record IssueStateDetails(
    Guid? WorkerRunId,
    string? BranchName,
    DateTimeOffset? FeedbackCutoffAt,
    string? FailureReason,
    DateTimeOffset? FailedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<BlockerDetails>? BlockedBy,
    TransientRetryDetails? TransientRetry,
    string? ChecksOutcome = null,
    IReadOnlyList<FailingCheck>? FailingChecks = null,
    string? RevisionTriggerKind = null,
    int? ChecksFixAttempts = null,
    string? RebaseOutcome = null,
    string? RebaseSkippedReason = null,
    int? RebaseConflictAttempts = null,
    AwaitingInputDetails? AwaitingInput = null,
    string? BranchUrl = null,
    string? SideBranchName = null,
    string? SideBranchUrl = null,
    string? DivergenceReason = null);

/// <summary>
/// An awaiting-input issue's open questions and how their post stands (ADR 0096): <paramref name="PostStatus"/> is
/// <c>pending</c>, <c>posted</c> (with <paramref name="CommentUrl"/> when known) or <c>failed</c> (with
/// <paramref name="PostFailureReason"/> and, for a transient failure, <paramref name="NextPostAttemptAt"/>).
/// <paramref name="ResumeTarget"/> is where input sends the issue: <c>revision</c>, <c>continuation</c>, <c>fresh</c> or
/// <c>retriage</c>.
/// </summary>
public sealed record AwaitingInputDetails(
    IReadOnlyList<InputQuestion> Questions,
    string PostStatus,
    DateTimeOffset? PostedAt,
    string? CommentUrl,
    string? PostFailureReason,
    DateTimeOffset? NextPostAttemptAt,
    string ResumeTarget);

/// <summary>
/// An open issue blocking this one. <paramref name="Reason"/> is the triage verdict's reason for a blocker it named,
/// and <see langword="null"/> for a blocker a person linked or when the latest verdict names none.
/// </summary>
public sealed record BlockerDetails(int IssueNumber, string? Reason);

public sealed record TransientRetryDetails(
    int AttemptNumber,
    int MaxAttempts,
    bool IsExhausted,
    DateTimeOffset? NextAttemptDueAt);
