using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.QueuedIssueTests;

public sealed class PolymorphicMembers
{
    // TierRank

    [Fact]
    public void WhenQueuedIssue_TierRankIsTwo()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();

        // Act
        int tierRank = queued.TierRank;

        // Assert
        tierRank.ShouldBe(2);
    }

    [Fact]
    public void WhenRevisionQueuedIssue_TierRankIsZero()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(42)
            .WithTitle("Add retry logic")
            .WithReviewComments([new ReviewComment("Please fix the formatting.")])
            .RevisionQueued();

        // Act
        int tierRank = revisionQueued.TierRank;

        // Assert
        tierRank.ShouldBe(0);
    }

    [Fact]
    public void WhenContinuationQueuedIssue_TierRankIsOne()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(42)
            .WithTitle("Add retry logic")
            .ContinuableFailed()
            .Retry();

        // Act
        int tierRank = continuationQueued.TierRank;

        // Assert
        tierRank.ShouldBe(1);
    }

    // DispatchBranchName

    [Fact]
    public void WhenQueuedIssue_DispatchBranchNameIsGenerated()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        BranchName expected = BranchName.Generate(queued.IssueKind.BranchPrefix, queued.IssueNumber, queued.Title);

        // Act
        BranchName branchName = queued.DispatchBranchName;

        // Assert
        branchName.ShouldBe(expected);
    }

    [Fact]
    public void WhenRevisionQueuedIssue_DispatchBranchNameIsExistingBranch()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithReviewComments([new ReviewComment("Please fix the formatting.")])
            .RevisionQueued();
        BranchName expected = BranchName.From(revisionQueued.BranchName);

        // Act
        BranchName branchName = revisionQueued.DispatchBranchName;

        // Assert
        branchName.ShouldBe(expected);
    }

    [Fact]
    public void WhenContinuationQueuedIssue_DispatchBranchNameIsExistingBranch()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ContinuableFailed()
            .Retry();
        BranchName expected = BranchName.From(continuationQueued.BranchName);

        // Act
        BranchName branchName = continuationQueued.DispatchBranchName;

        // Assert
        branchName.ShouldBe(expected);
    }

    // Context

    [Fact]
    public void WhenQueuedIssue_ContextIsFresh()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        BranchName branchName = BranchName.Generate(queued.IssueKind.BranchPrefix, queued.IssueNumber, queued.Title);

        // Act
        DispatchContext context = queued.Context;

        // Assert
        DispatchContext.Fresh fresh = context.ShouldBeOfType<DispatchContext.Fresh>();
        fresh.BranchName.ShouldBe(branchName.Value);
    }

    [Fact]
    public void WhenRevisionQueuedIssue_ContextIsRevision()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionQueued();

        // Act
        DispatchContext context = revisionQueued.Context;

        // Assert
        DispatchContext.Revision revision = context.ShouldBeOfType<DispatchContext.Revision>();
        revision.ShouldSatisfyAllConditions(
            () => revision.BranchName.ShouldBe(revisionQueued.BranchName),
            () => revision.PullRequestUrl.ShouldBe(revisionQueued.PullRequestUrl),
            () => revision.Comments.ShouldBe(revisionQueued.ReviewComments));
    }

    [Fact]
    public void WhenContinuationQueuedIssue_ContextIsContinuation()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ContinuableFailed()
            .Retry();

        // Act
        DispatchContext context = continuationQueued.Context;

        // Assert
        DispatchContext.Continuation continuation = context.ShouldBeOfType<DispatchContext.Continuation>();
        continuation.ShouldSatisfyAllConditions(
            () => continuation.BranchName.ShouldBe(continuationQueued.BranchName),
            () => continuation.FailureReason.ShouldBe(continuationQueued.FailureReason));
    }

    // Claim — typed return and domain event

    [Fact]
    public void WhenQueuedIssueClaimed_ReturnsInProgressIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        WorkerRunId workerRunId = WorkerRunId.New();

        // Act
        Issue claimed = queued.Claim(workerRunId);

        // Assert
        claimed.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public void WhenQueuedIssueClaimed_RaisesIssueInProgressEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();

        // Act
        queued.Claim(WorkerRunId.New());

        // Assert
        queued.DomainEvents.ShouldContain(e => e is IssueInProgress);
    }

    [Fact]
    public void WhenRevisionQueuedIssueClaimed_ReturnsRevisionInProgressIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionQueued();
        WorkerRunId workerRunId = WorkerRunId.New();

        // Act
        Issue claimed = revisionQueued.Claim(workerRunId);

        // Assert
        claimed.ShouldBeOfType<RevisionInProgressIssue>();
    }

    [Fact]
    public void WhenRevisionQueuedIssueClaimed_RaisesIssueRevisionInProgressEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionQueued();

        // Act
        revisionQueued.Claim(WorkerRunId.New());

        // Assert
        revisionQueued.DomainEvents.ShouldContain(e => e is IssueRevisionInProgress);
    }

    [Fact]
    public void WhenContinuationQueuedIssueClaimed_ReturnsInProgressIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ContinuableFailed()
            .Retry();
        WorkerRunId workerRunId = WorkerRunId.New();

        // Act
        Issue claimed = continuationQueued.Claim(workerRunId);

        // Assert
        claimed.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public void WhenContinuationQueuedIssueClaimed_RaisesIssueInProgressEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ContinuableFailed()
            .Retry();

        // Act
        continuationQueued.Claim(WorkerRunId.New());

        // Assert
        continuationQueued.DomainEvents.ShouldContain(e => e is IssueInProgress);
    }
}
