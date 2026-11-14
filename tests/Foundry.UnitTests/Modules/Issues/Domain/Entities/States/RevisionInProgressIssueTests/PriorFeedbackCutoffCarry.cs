using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionInProgressIssueTests;

/// <summary>
/// A fix run that fails, is blocked, or fails to publish keeps the cutoff it started from through retry, so the
/// retried run still ends with that cutoff instead of its own start time (issue #925).
/// </summary>
public sealed class PriorFeedbackCutoffCarry
{
    private static readonly DateTimeOffset PriorCutoffAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RetriedRunStartedAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenFixRunFailsAndIsRetried_RetriedRunKeepsThePriorCutoff()
    {
        // Arrange
        RevisionFailedIssue failed = FixRun().MarkFailed("exit 1", FailureCategory.NonZeroExit, DateTimeOffset.UtcNow);

        // Act
        RevisionInProgressIssue retried = failed.Retry(DateTimeOffset.UtcNow).Claim(WorkerRunId.New(), RetriedRunStartedAt);

        // Assert
        Complete(retried).ShouldBe(PriorCutoffAt);
    }

    [Fact]
    public void WhenFixRunPublishFailsAndIsRetried_RetriedPublishKeepsThePriorCutoff()
    {
        // Arrange
        PublishFailedIssue publishFailed = FixRun().MarkPublishFailed("push rejected", DateTimeOffset.UtcNow);

        // Act
        RevisionInProgressIssue retried = publishFailed.RetryPublish(DateTimeOffset.UtcNow).ShouldBeOfType<RevisionInProgressIssue>();

        // Assert
        Complete(retried).ShouldBe(PriorCutoffAt);
    }

    [Fact]
    public void WhenFixRunPublishFailsThenFailsAndIsRetried_RetriedRunKeepsThePriorCutoff()
    {
        // Arrange
        PublishFailedIssue publishFailed = FixRun().MarkPublishFailed("push rejected", DateTimeOffset.UtcNow);
        RevisionFailedIssue failed = publishFailed
            .Fail("discarded", FailureCategory.RunDiscarded, DateTimeOffset.UtcNow)
            .ShouldBeOfType<RevisionFailedIssue>();

        // Act
        RevisionInProgressIssue retried = failed.Retry(DateTimeOffset.UtcNow).Claim(WorkerRunId.New(), RetriedRunStartedAt);

        // Assert
        Complete(retried).ShouldBe(PriorCutoffAt);
    }

    [Fact]
    public void WhenFixRunIsBlockedThenFailsAndIsRetried_RetriedRunKeepsThePriorCutoff()
    {
        // Arrange
        PublishBlockedIssue blocked = FixRun().MarkPublishBlocked(DateTimeOffset.UtcNow);
        RevisionFailedIssue failed = blocked
            .Fail("discarded", FailureCategory.RunDiscarded, DateTimeOffset.UtcNow)
            .ShouldBeOfType<RevisionFailedIssue>();

        // Act
        RevisionInProgressIssue retried = failed.Retry(DateTimeOffset.UtcNow).Claim(WorkerRunId.New(), RetriedRunStartedAt);

        // Assert
        Complete(retried).ShouldBe(PriorCutoffAt);
    }

    private static RevisionInProgressIssue FixRun() =>
        new IssueBuilder()
            .WithFeedbackCutoffAt(PriorCutoffAt)
            .ChecksPending()
            .FixChecks([new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1")], DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);

    private static DateTimeOffset Complete(RevisionInProgressIssue revisionInProgress) =>
        revisionInProgress.MarkRebasePending(RetriedRunStartedAt, DateTimeOffset.UtcNow).FeedbackCutoffAt;
}
