using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.PublishBlockedIssueTests;

public sealed class Transitions
{
    private static readonly DateTimeOffset DiscardedAt = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ClosedAt = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnImplementRunBlocked_KeepsTheRunAndBranch()
    {
        // Arrange
        InProgressIssue inProgress = new IssueBuilder().InProgress();

        // Act
        PublishBlockedIssue blocked = inProgress.MarkPublishBlocked("feat/1-test", DiscardedAt);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => blocked.BranchName.ShouldBe("feat/1-test"),
            () => blocked.BlockedAt.ShouldBe(DiscardedAt),
            () => blocked.IsRevision.ShouldBeFalse(),
            () => blocked.IsRestingState().ShouldBeFalse(),
            () => inProgress.DomainEvents.ShouldHaveSingleItem());
    }

    [Fact]
    public void ARevisionBlocked_KeepsItsTriggerAndPullRequest()
    {
        // Arrange
        RevisionInProgressIssue revision = new IssueBuilder()
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .RevisionInProgress();

        // Act
        PublishBlockedIssue blocked = revision.MarkPublishBlocked(DiscardedAt);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.IsRevision.ShouldBeTrue(),
            () => blocked.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"),
            () => blocked.LinkedPullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"));
    }

    [Fact]
    public void ADiscardedImplementRun_FailsContinuablyOnItsBranch()
    {
        // Arrange
        PublishBlockedIssue blocked = new IssueBuilder().WithBranchName("feat/1-test").PublishBlocked();

        // Act
        Issue failed = blocked.Fail("discarded", FailureCategory.RunDiscarded, DiscardedAt);

        // Assert
        ContinuableFailedIssue continuable = failed.ShouldBeOfType<ContinuableFailedIssue>();
        continuable.ShouldSatisfyAllConditions(
            () => continuable.BranchName.ShouldBe("feat/1-test"),
            () => continuable.FailureCategory.ShouldBe(FailureCategory.RunDiscarded),
            () => continuable.WorkerRunId.ShouldBe(blocked.WorkerRunId));
    }

    [Fact]
    public void ADiscardedRevision_FailsAsARevision()
    {
        // Arrange
        PublishBlockedIssue blocked = new IssueBuilder().PublishBlockedFromRevision();

        // Act
        Issue failed = blocked.Fail("discarded", FailureCategory.RunDiscarded, DiscardedAt);

        // Assert
        failed.ShouldBeOfType<RevisionFailedIssue>().FailureCategory.ShouldBe(FailureCategory.RunDiscarded);
    }

    [Fact]
    public void WhenProviderIssueClosesOnAnImplementRun_CompletesOnItsBranchWithNoPullRequest()
    {
        // Arrange
        PublishBlockedIssue blocked = new IssueBuilder().WithBranchName("feat/1-test").PublishBlocked();

        // Act
        CompletedIssue completed = blocked.Complete(ClosedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.Id.ShouldBe(blocked.Id),
            () => completed.BranchName.ShouldBe("feat/1-test"),
            () => completed.PullRequestUrl.ShouldBeEmpty(),
            () => completed.CompletedAt.ShouldBe(ClosedAt),
            () => completed.EnteredStateAt.ShouldBe(ClosedAt));
    }

    [Fact]
    public void WhenProviderIssueClosesOnARevision_CompletesWithTheRevisedPullRequest()
    {
        // Arrange
        PublishBlockedIssue blocked = new IssueBuilder()
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .PublishBlockedFromRevision();

        // Act
        CompletedIssue completed = blocked.Complete(ClosedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe(blocked.BranchName),
            () => completed.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"));
    }

    [Fact]
    public void WhenProviderIssueCloses_RaisesIssueCompletedAndIssueClosedWithUnpublishedRun()
    {
        // Arrange
        PublishBlockedIssue blocked = new IssueBuilder().PublishBlocked();

        // Act
        blocked.Complete(ClosedAt);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.DomainEvents.Count.ShouldBe(2),
            () => blocked.DomainEvents.OfType<IssueCompleted>().ShouldHaveSingleItem().IssueId.ShouldBe(blocked.Id),
            () => blocked.DomainEvents.OfType<IssueClosedWithUnpublishedRun>().ShouldHaveSingleItem()
                .WorkerRunId.ShouldBe(blocked.WorkerRunId));
    }
}
