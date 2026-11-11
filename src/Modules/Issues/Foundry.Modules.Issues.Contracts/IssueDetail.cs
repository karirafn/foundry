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
    IssueStateDetails? StateDetails);

public sealed record IssueStateDetails(
    Guid? WorkerRunId,
    string? BranchName,
    DateTimeOffset? FeedbackCutoffAt,
    string? FailureReason,
    DateTimeOffset? FailedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<int>? BlockedBy,
    TransientRetryDetails? TransientRetry,
    string? ChecksOutcome = null,
    IReadOnlyList<FailingCheck>? FailingChecks = null,
    string? RevisionTriggerKind = null,
    int? ChecksFixAttempts = null,
    string? RebaseOutcome = null,
    string? RebaseSkippedReason = null,
    int? RebaseConflictAttempts = null,
    AwaitingInputDetails? AwaitingInput = null);

/// <summary>
/// An awaiting-input issue's open questions and how their post stands (ADR 0096): <paramref name="PostStatus"/> is
/// <c>pending</c>, <c>posted</c> (with <paramref name="CommentUrl"/> when known) or <c>failed</c> (with
/// <paramref name="PostFailureReason"/> and, for a transient failure, <paramref name="NextPostAttemptAt"/>).
/// </summary>
public sealed record AwaitingInputDetails(
    IReadOnlyList<InputQuestion> Questions,
    string PostStatus,
    DateTimeOffset? PostedAt,
    string? CommentUrl,
    string? PostFailureReason,
    DateTimeOffset? NextPostAttemptAt);

public sealed record TransientRetryDetails(
    int AttemptNumber,
    int MaxAttempts,
    bool IsExhausted,
    DateTimeOffset? NextAttemptDueAt);
