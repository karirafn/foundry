using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using WorkerRunFailedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

namespace Foundry.Modules.Workers.Domain.Entities.States;

/// <summary>
/// A run whose container has ended but whose commits are not yet published (ADR 0100): it is publishing, or waiting to
/// retry a failed publish. It keeps everything outcome resolution needs, because the container is already gone.
/// </summary>
public abstract class UnpublishedRun : WorkerRun, IResolvableRun
{
    // Parameterless constructor for EF Core materialization.
    protected UnpublishedRun()
    {
    }

    protected UnpublishedRun(UnpublishedRun source)
        : base(source.Id, source.IssueId, source.CreatedAt, source.Role, source.Image)
    {
        CopyFrom(source);
    }

    private protected UnpublishedRun(
        ActiveRun active,
        BranchName branchName,
        RunExit exit,
        string? containerOutput,
        RunPullRequestText? pullRequestText,
        WorkerRunTranscript? transcript)
        : base(active.Id, active.IssueId, active.CreatedAt, active.Role, active.Image)
    {
        StartedAt = active.StartedAt;
        BranchName = branchName;
        MonitoredRepositoryId = active.MonitoredRepositoryId;
        CancelRequestedAt = active.CancelRequestedAt;
        ExitCode = exit.ExitCode;
        OomKilled = exit.OomKilled;
        StopReason = exit.StopReason;
        ExitedAt = exit.ExitedAt;
        ContainerOutput = containerOutput;
        PullRequestSummary = pullRequestText?.Summary;
        PullRequestBody = pullRequestText?.Body;
        PendingTranscript = transcript?.Transcript;
        PendingTranscriptCapturedAt = transcript?.CapturedAt;
        RecordBase(active.Base);
    }

    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>The run's branch: only a run with a branch has commits to publish.</summary>
    public BranchName BranchName { get; private set; }

    BranchName? IResolvableRun.BranchName => BranchName;

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public DateTimeOffset? CancelRequestedAt { get; private set; }

    public int? ExitCode { get; private set; }

    public bool OomKilled { get; private set; }

    public RunStopReason StopReason { get; private set; }

    public DateTimeOffset ExitedAt { get; private set; }

    public RunExit Exit => new(ExitCode, OomKilled, StopReason, ExitedAt);

    /// <summary>The redacted container log tail captured when the container ended; outcome classification reads it.</summary>
    public string? ContainerOutput { get; private set; }

    /// <summary>The verdict's <c>pullRequest.summary</c> as the run wrote it, scrubbed only when it is used.</summary>
    public string? PullRequestSummary { get; private set; }

    /// <summary>The verdict's <c>pullRequest.body</c> as the run wrote it, scrubbed only when it is used.</summary>
    public string? PullRequestBody { get; private set; }

    /// <summary>Failed publish attempts since the run last started publishing from an operator's request or its exit.</summary>
    public int FailedPublishAttempts { get; private protected set; }

    /// <summary>The subjects of the commits the Publisher pushed, oldest first, kept for a pull request body fallback.</summary>
    public IReadOnlyList<string> PublishedCommitSubjects { get; private set; } = [];

    /// <summary>When the Publisher finished; <see langword="null"/> while the push is still to do.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// When the pull request step finished (opened one, commented on one, or had nothing to do), so a retry after a later
    /// step failed never opens or comments twice.
    /// </summary>
    public DateTimeOffset? PullRequestSettledAt { get; private protected set; }

    /// <summary>
    /// The Run Transcript captured when the container ended, held on the run until it is terminal: a transcript row is
    /// keyed to the run row, which every state transition replaces.
    /// </summary>
    public string? PendingTranscript { get; private set; }

    public DateTimeOffset? PendingTranscriptCapturedAt { get; private set; }

    /// <summary>When an unpublished run's Hand-off volume is deleted and the run discarded.</summary>
    public DateTimeOffset DiscardsAt => ExitedAt + PublishRetryPolicy.HandOffTimeToLive;

    /// <summary>The Run Transcript to store with the terminal state, or <see langword="null"/> when none was captured.</summary>
    public WorkerRunTranscript? TranscriptForTerminalState() =>
        PendingTranscript is { } transcript && PendingTranscriptCapturedAt is { } capturedAt
            ? WorkerRunTranscript.Capture(Id, transcript, capturedAt)
            : null;

    public RunPullRequestText? PullRequestText =>
        PullRequestSummary is null && PullRequestBody is null ? null : new RunPullRequestText(PullRequestSummary, PullRequestBody);

    /// <summary>Ends the run without publishing: its unpublished work is discarded for <paramref name="reason"/>.</summary>
    private protected FailedRun DiscardWith(FailureReason reason)
    {
        FailedRun failed = FailedRun.FromUnpublished(this, reason, ContainerOutput, resultSummary: null, Verdict);
        AddDomainEvent(new WorkerRunFailedEvent(Id, IssueId, reason.Summary, reason.CategoryToken, BranchName.Value));
        return failed;
    }

    private protected void RecordPublishResult(PublishResult result, DateTimeOffset publishedAt)
    {
        switch (result)
        {
            case PublishResult.Published published:
                RecordPublishFacts(published.Head, published.BranchCommitCount, divergedSideBranch: null, published.RevertedFiles);
                PublishedCommitSubjects = published.CommitSubjects;
                break;
            case PublishResult.Diverged diverged:
                RecordPublishFacts(pushedHead: null, branchCommitCount: null, diverged.SideBranch, diverged.RevertedFiles);
                break;
            case PublishResult.NothingToPublish:
                break;
            default:
                throw new ArgumentException($"A {result.GetType().Name} publish result is not a finished publish.", nameof(result));
        }

        PublishedAt = publishedAt;
    }

    private void CopyFrom(UnpublishedRun source)
    {
        StartedAt = source.StartedAt;
        BranchName = source.BranchName;
        MonitoredRepositoryId = source.MonitoredRepositoryId;
        CancelRequestedAt = source.CancelRequestedAt;
        ExitCode = source.ExitCode;
        OomKilled = source.OomKilled;
        StopReason = source.StopReason;
        ExitedAt = source.ExitedAt;
        ContainerOutput = source.ContainerOutput;
        PullRequestSummary = source.PullRequestSummary;
        PullRequestBody = source.PullRequestBody;
        FailedPublishAttempts = source.FailedPublishAttempts;
        PublishedCommitSubjects = source.PublishedCommitSubjects;
        PublishedAt = source.PublishedAt;
        PullRequestSettledAt = source.PullRequestSettledAt;
        PendingTranscript = source.PendingTranscript;
        PendingTranscriptCapturedAt = source.PendingTranscriptCapturedAt;
        RecordBase(source.Base);
        RecordVerdict(source.Verdict);
        RecordPublishFacts(source.PublishedHead, source.PublishedBranchCommitCount, source.DivergedSideBranch, source.RevertedFiles);
    }
}
