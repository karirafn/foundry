using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.InProgressIssueTests;

/// <summary>
/// A running implement issue remembers whether its run is fresh or a continuation (#1060), so the board can show its
/// run kind, and keeps that origin across a publish that is blocked or fails and then resumes.
/// </summary>
public sealed class Origin
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClaimedFromFreshQueue_IsAFreshRun()
    {
        // Arrange
        FreshQueuedIssue queued = new IssueBuilder().FreshQueued();

        // Act
        InProgressIssue inProgress = queued.Claim(WorkerRunId.New(), Now);

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.Origin.ShouldBe(ImplementRunOrigin.Fresh),
            () => inProgress.RunKind.ShouldBe(RunKind.Fresh));
    }

    [Fact]
    public void ClaimedFromContinuationQueue_IsAContinuationRun()
    {
        // Arrange
        ContinuationQueuedIssue queued = new IssueBuilder().ContinuableFailed().Retry(Now);

        // Act
        InProgressIssue inProgress = queued.Claim(WorkerRunId.New(), Now);

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.Origin.ShouldBe(ImplementRunOrigin.Continuation),
            () => inProgress.RunKind.ShouldBe(RunKind.Continuation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThroughPublishBlocked_KeepsItsOrigin(bool continuation)
    {
        // Arrange
        InProgressIssue inProgress = ClaimedRun(continuation);
        PublishBlockedIssue blocked = inProgress.MarkPublishBlocked("feat/1-test", Now);

        // Act
        Issue resumed = blocked.ConfirmFalsePositives(Now);

        // Assert
        resumed.ShouldBeOfType<InProgressIssue>().Origin.ShouldBe(inProgress.Origin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThroughPublishFailed_KeepsItsOrigin(bool continuation)
    {
        // Arrange
        InProgressIssue inProgress = ClaimedRun(continuation);
        PublishFailedIssue publishFailed = inProgress.MarkPublishFailed("feat/1-test", "push refused", Now);

        // Act
        Issue resumed = publishFailed.RetryPublish(Now);

        // Assert
        resumed.ShouldBeOfType<InProgressIssue>().Origin.ShouldBe(inProgress.Origin);
    }

    [Fact]
    public void ARevisionPublishBlocked_HasNoImplementOrigin()
    {
        // Arrange
        RevisionInProgressIssue revision = new IssueBuilder().RevisionInProgress();

        // Act
        PublishBlockedIssue blocked = revision.MarkPublishBlocked(Now);

        // Assert
        blocked.Origin.ShouldBeNull();
    }

    [Fact]
    public void ARevisionPublishFailed_HasNoImplementOrigin()
    {
        // Arrange
        RevisionInProgressIssue revision = new IssueBuilder().RevisionInProgress();

        // Act
        PublishFailedIssue publishFailed = revision.MarkPublishFailed("push refused", Now);

        // Assert
        publishFailed.Origin.ShouldBeNull();
    }

    private static InProgressIssue ClaimedRun(bool continuation) =>
        continuation
            ? new IssueBuilder().ContinuableFailed().Retry(Now).Claim(WorkerRunId.New(), Now)
            : new IssueBuilder().InProgress();
}
