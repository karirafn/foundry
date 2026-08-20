using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ClaimableIssueTests;

public sealed class PolymorphicMembers
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static DetectedIssue CreateDetected(MonitoredRepositoryId repositoryId) =>
        DetectedIssue.Detect(
            repositoryId,
            issueNumber: 42,
            title: "Add retry logic",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);

    private static QueuedIssue CreateQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = CreateDetected(repositoryId);
        return detected.Enqueue();
    }

    private static RevisionQueuedIssue CreateRevisionQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = CreateDetected(repositoryId);
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/42-add-retry-logic",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);
        return review.Revise([new ReviewComment("Please fix the formatting.")]);
    }

    private static ContinuationQueuedIssue CreateContinuationQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = CreateDetected(repositoryId);
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "feat/42-add-retry-logic",
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);
        return failed.Retry();
    }

    // TierRank

    [Fact]
    public void WhenQueuedIssue_TierRankIsTwo()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);

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
        RevisionQueuedIssue revisionQueued = CreateRevisionQueuedIssue(repositoryId);

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
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);

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
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
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
        RevisionQueuedIssue revisionQueued = CreateRevisionQueuedIssue(repositoryId);
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
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);
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
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
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
        RevisionQueuedIssue revisionQueued = CreateRevisionQueuedIssue(repositoryId);

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
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);

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
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

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
        QueuedIssue queued = CreateQueuedIssue(repositoryId);

        // Act
        queued.Claim(Guid.NewGuid());

        // Assert
        queued.DomainEvents.ShouldContain(e => e is IssueInProgress);
    }

    [Fact]
    public void WhenRevisionQueuedIssueClaimed_ReturnsRevisionInProgressIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = CreateRevisionQueuedIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

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
        RevisionQueuedIssue revisionQueued = CreateRevisionQueuedIssue(repositoryId);

        // Act
        revisionQueued.Claim(Guid.NewGuid());

        // Assert
        revisionQueued.DomainEvents.ShouldContain(e => e is IssueRevisionInProgress);
    }

    [Fact]
    public void WhenContinuationQueuedIssueClaimed_ReturnsInProgressIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

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
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);

        // Act
        continuationQueued.Claim(Guid.NewGuid());

        // Assert
        continuationQueued.DomainEvents.ShouldContain(e => e is IssueInProgress);
    }
}
