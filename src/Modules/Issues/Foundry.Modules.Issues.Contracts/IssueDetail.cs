using System.Text.Json.Serialization;

using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// One issue in full. <paramref name="RunKind"/> is the kind of run the issue is queued for or running (an
/// <see cref="IssueSummary"/> run kind), and <see langword="null"/> when none is; while a run publishes, it is that run's kind.
/// </summary>
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
    TriageVerdictDetails? Triage,
    IReadOnlyList<ClarificationDetails> Clarifications,
    string? RunKind);

/// <summary>
/// One round of the issue's clarification log (ADR 0096), oldest first in <see cref="IssueDetail.Clarifications"/>:
/// the questions a run asked and when and, once the round closed, the input that answered it and when it arrived.
/// <paramref name="Input"/> and <paramref name="AnsweredAt"/> are both <see langword="null"/> while the round is open.
/// </summary>
public sealed record ClarificationDetails(
    IReadOnlyList<InputQuestion> Questions,
    IReadOnlyList<InputPartDetails>? Input,
    DateTimeOffset AskedAt,
    DateTimeOffset? AnsweredAt);

/// <summary>One part of a clarification round's input, as the dashboard renders it.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Comments), typeDiscriminator: "comments")]
[JsonDerivedType(typeof(DescriptionEdited), typeDiscriminator: "descriptionEdited")]
[JsonDerivedType(typeof(BranchUpdated), typeDiscriminator: "branchUpdated")]
[JsonDerivedType(typeof(ContinuedWithoutAnswer), typeDiscriminator: "continuedWithoutAnswer")]
public abstract record InputPartDetails
{
    private InputPartDetails() { }

    /// <summary>The settled comments' text. It is untrusted, so it renders as text, never as HTML.</summary>
    public sealed record Comments(string Text) : InputPartDetails;

    /// <summary>The issue description was edited after the question was posted.</summary>
    public sealed record DescriptionEdited : InputPartDetails;

    /// <summary>
    /// Someone moved the branch from <paramref name="FromSha"/> (<see langword="null"/> when the branch did not exist
    /// when the question was posted) to <paramref name="ToSha"/>. <paramref name="CompareUrl"/> is the provider's
    /// compare view of the move, or the new head's commit page when there is no from SHA, and <see langword="null"/>
    /// when the issue URL has no known provider shape.
    /// </summary>
    public sealed record BranchUpdated(string? FromSha, string ToSha, string? CompareUrl) : InputPartDetails;

    /// <summary>No answer arrived, and an operator chose to continue anyway.</summary>
    public sealed record ContinuedWithoutAnswer : InputPartDetails;
}

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
/// (<c>branch-moved</c> or <c>force-push-protected</c>). <paramref name="ChecksPendingReason"/> is set only for
/// <c>checks_pending</c>: <c>after_run</c>, <c>head_moved</c>, <c>checks_rerun</c> or <c>catch_up</c>.
/// <paramref name="MergeOutcome"/> and <paramref name="MergeSkippedReason"/> are set only for a <c>review</c> issue that
/// left merge-pending without a merge (ADR 0105). <paramref name="RebasePendingReason"/> is set only for
/// <c>rebase_pending</c>: <c>after_run</c> or <c>catch_up</c>. <paramref name="CatchUpRound"/> and
/// <paramref name="CatchUpRoundLimit"/> are set while a catch-up (ADR 0111) has started: in <c>rebase_pending</c> or
/// <c>checks_pending</c> with reason <c>catch_up</c>, or in <c>merge_pending</c>. <paramref name="MergeWait"/> is set
/// only for a <c>merge_pending</c> issue waiting in the catch-up queue.
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
    string? DivergenceReason = null,
    string? ChecksPendingReason = null,
    string? MergeOutcome = null,
    string? MergeSkippedReason = null,
    string? RebasePendingReason = null,
    int? CatchUpRound = null,
    int? CatchUpRoundLimit = null,
    MergeWaitDetails? MergeWait = null);

/// <summary>
/// A merge-pending issue's place in its repository's catch-up queue (ADR 0111): <paramref name="Position"/> is 1-based,
/// 1 meaning next to catch up, and <paramref name="WaitingForIssueNumber"/> is the issue whose up-to-date pull request
/// merges first, when the queue head waits for one.
/// </summary>
public sealed record MergeWaitDetails(int Position, int? WaitingForIssueNumber);

/// <summary>
/// An awaiting-input issue's open questions and how their post stands (ADR 0096): <paramref name="PostStatus"/> is
/// <c>pending</c>, <c>posted</c> (with <paramref name="CommentUrl"/> when known) or <c>failed</c> (with
/// <paramref name="PostFailureReason"/> and, for a transient failure, <paramref name="NextPostAttemptAt"/>).
/// <paramref name="ResumeTarget"/> is where input sends the issue: <c>revision</c>, <c>continuation</c>, <c>fresh</c> or
/// <c>retriage</c>.
/// <paramref name="ReminderSentAt"/> is when the round's one reminder was posted, <see langword="null"/> until then.
/// <paramref name="ReminderDueAt"/> is when the reminder is due: the reminder delay after the post, or the retry time
/// after a failed reminder post. It is <see langword="null"/> while the question is unposted, once the reminder is sent,
/// and after its post failed permanently.
/// </summary>
public sealed record AwaitingInputDetails(
    IReadOnlyList<InputQuestion> Questions,
    string PostStatus,
    DateTimeOffset? PostedAt,
    string? CommentUrl,
    string? PostFailureReason,
    DateTimeOffset? NextPostAttemptAt,
    string ResumeTarget,
    DateTimeOffset? ReminderSentAt,
    DateTimeOffset? ReminderDueAt);

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
