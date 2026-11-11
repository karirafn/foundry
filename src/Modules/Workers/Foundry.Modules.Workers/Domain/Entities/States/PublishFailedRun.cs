using Foundry.Modules.Workers.Domain.ValueObjects;

using WorkerRunFailedEvent = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

namespace Foundry.Modules.Workers.Domain.Entities.States;

/// <summary>
/// A run whose last publish attempt failed (ADR 0100). Under the retry cap it waits for its next attempt and keeps its
/// dispatch slot; once exhausted it waits for an operator's Retry publish, keeping its Hand-off volume until the time
/// to live discards it.
/// </summary>
public sealed class PublishFailedRun : UnpublishedRun
{
    // Private parameterless constructor for EF Core materialization.
    private PublishFailedRun()
    {
    }

    private PublishFailedRun(PublishingRun source)
        : base(source)
    {
    }

    /// <summary>Why the last attempt failed, as the Publisher or the provider reported it (redacted).</summary>
    public string PublishFailureReason { get; private set; } = string.Empty;

    public DateTimeOffset PublishFailedAt { get; private set; }

    /// <summary>When Foundry retries on its own; <see langword="null"/> once the retries are exhausted.</summary>
    public DateTimeOffset? NextPublishAttemptAt { get; private set; }

    public bool IsExhausted => NextPublishAttemptAt is null;

    internal static PublishFailedRun FromPublishing(
        PublishingRun publishing,
        int failedAttempts,
        string reason,
        DateTimeOffset failedAt,
        DateTimeOffset? nextAttemptAt)
    {
        PublishFailedRun failed = new(publishing)
        {
            PublishFailureReason = reason,
            PublishFailedAt = failedAt,
            NextPublishAttemptAt = nextAttemptAt,
        };
        failed.FailedPublishAttempts = failedAttempts;
        return failed;
    }

    public bool IsRetryDue(DateTimeOffset now) => NextPublishAttemptAt is { } next && next <= now;

    /// <summary>The scheduled retry is due: publish again, counting toward the cap.</summary>
    public PublishingRun RetryPublish(DateTimeOffset now)
    {
        if (!IsRetryDue(now))
        {
            throw new InvalidOperationException($"Worker run '{Id.Value}' has no publish retry due.");
        }

        return PublishingRun.FromPublishFailed(this, resetAttempts: false);
    }

    /// <summary>An operator asked to publish again after the retries were exhausted: a fresh set of attempts.</summary>
    public PublishingRun RequestRetry()
    {
        if (!IsExhausted)
        {
            throw new InvalidOperationException($"Worker run '{Id.Value}' is still retrying its publish.");
        }

        return PublishingRun.FromPublishFailed(this, resetAttempts: true);
    }

    /// <summary>The Hand-off volume reached its time to live: the unpublished work is discarded and the run fails.</summary>
    public FailedRun Discard()
    {
        FailureReason reason = new FailureReason.PublishExpired();
        FailedRun failed = FailedRun.FromUnpublished(this, reason, ContainerOutput, resultSummary: null, Verdict);
        AddDomainEvent(new WorkerRunFailedEvent(Id, IssueId, reason.Summary, reason.CategoryToken, BranchName.Value));
        return failed;
    }
}
