using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using WorkerRunCompletedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunCompleted;
using WorkerRunFailedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

namespace Foundry.Modules.Workers.Domain.Entities.States;

/// <summary>
/// A run whose container ended and whose commits Foundry is publishing (ADR 0100): the Publisher pushes the Run bundle,
/// then code opens or updates the pull request and resolves the outcome. The run holds its dispatch slot until then.
/// </summary>
public sealed class PublishingRun : UnpublishedRun, ITerminableRun
{
    // Private parameterless constructor for EF Core materialization.
    private PublishingRun()
    {
    }

    private PublishingRun(UnpublishedRun source)
        : base(source)
    {
    }

    private PublishingRun(
        ActiveRun active,
        BranchName branchName,
        RunExit exit,
        string? containerOutput,
        RunVerdict? verdict,
        RunPullRequestText? pullRequestText)
        : base(active, branchName, exit, containerOutput, pullRequestText)
    {
        RecordVerdict(verdict);
    }

    /// <summary>Whether the Publisher already finished, so only the pull request and outcome steps remain.</summary>
    public bool HasPublished => PublishedAt.HasValue;

    internal static PublishingRun FromActive(
        ActiveRun active,
        BranchName branchName,
        RunExit exit,
        string? containerOutput,
        RunVerdict? verdict,
        RunPullRequestText? pullRequestText) =>
        new(active, branchName, exit, containerOutput, verdict, pullRequestText);

    internal static PublishingRun FromPublishFailed(PublishFailedRun failed, bool resetAttempts)
    {
        PublishingRun publishing = new(failed);

        if (resetAttempts)
        {
            publishing.FailedPublishAttempts = 0;
        }

        return publishing;
    }

    /// <summary>Records what the Publisher pushed; a failed publish goes through <see cref="FailPublish"/> instead.</summary>
    public void RecordPublished(PublishResult result, DateTimeOffset publishedAt)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result is PublishResult.Failed)
        {
            throw new ArgumentException("A failed publish is recorded with FailPublish.", nameof(result));
        }

        RecordPublishResult(result, publishedAt);
    }

    /// <summary>Records that the pull request step is done, so a retried publish never repeats it.</summary>
    public void RecordPullRequestSettled(DateTimeOffset settledAt) => PullRequestSettledAt ??= settledAt;

    /// <summary>
    /// A publish attempt failed. A transient failure under the attempt cap is retried after the policy's delay; any
    /// other failure waits for an operator's Retry publish.
    /// </summary>
    public PublishFailedRun FailPublish(string reason, bool transient, DateTimeOffset failedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        int attempts = FailedPublishAttempts + 1;
        DateTimeOffset? nextAttemptAt = transient && attempts < PublishRetryPolicy.MaxAttempts
            ? failedAt + PublishRetryPolicy.RetryDelays[attempts - 1]
            : null;

        return PublishFailedRun.FromPublishing(this, attempts, reason, failedAt, nextAttemptAt);
    }

    public CompletedRun Complete(
        int exitCode,
        BranchName? branchName,
        PullRequestUrl? pullRequestUrl,
        RunResultSummary? resultSummary = null,
        RunVerdict? verdict = null,
        string? containerOutput = null)
    {
        CompletedRun completed = CompletedRun.FromPublishing(
            this,
            exitCode,
            branchName,
            pullRequestUrl,
            resultSummary,
            verdict ?? Verdict,
            containerOutput);
        AddDomainEvent(new WorkerRunCompletedEvent(Id, IssueId, branchName?.Value, pullRequestUrl?.Value));
        return completed;
    }

    public FailedRun Fail(
        FailureReason reason,
        BranchName? branchNameOrNull,
        string? containerOutput = null,
        RunResultSummary? resultSummary = null,
        RunVerdict? verdict = null)
    {
        FailedRun failed = FailedRun.FromUnpublished(this, reason, containerOutput, resultSummary, verdict ?? Verdict);
        AddDomainEvent(new WorkerRunFailedEvent(Id, IssueId, reason.Summary, reason.CategoryToken, branchNameOrNull?.Value));
        return failed;
    }
}
