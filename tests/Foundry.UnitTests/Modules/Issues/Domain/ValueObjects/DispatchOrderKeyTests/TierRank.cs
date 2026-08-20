using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchOrderKeyTests;

public sealed class TierRank
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static DetectedIssue CreateDetected(MonitoredRepositoryId repositoryId) =>
        DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);

    private static QueuedIssue CreateFreshQueued()
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetected(repositoryId);
        return detected.Enqueue();
    }

    private static RevisionQueuedIssue CreateRevisionQueued()
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetected(repositoryId);
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/1-test-issue",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);
        return review.Revise([new ReviewComment("Please fix the formatting.")]);
    }

    private static ContinuationQueuedIssue CreateContinuationQueued()
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetected(repositoryId);
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "feat/1-test-issue",
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);
        return failed.Retry();
    }

    [Fact]
    public void WhenRevisionQueued_TierRankIsZero()
    {
        // Arrange
        RevisionQueuedIssue revisionQueued = CreateRevisionQueued();

        // Act
        int tierRank = revisionQueued.TierRank;

        // Assert
        tierRank.ShouldBe(0);
    }

    [Fact]
    public void WhenContinuationQueued_TierRankIsOne()
    {
        // Arrange
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueued();

        // Act
        int tierRank = continuationQueued.TierRank;

        // Assert
        tierRank.ShouldBe(1);
    }

    [Fact]
    public void WhenFreshQueued_TierRankIsTwo()
    {
        // Arrange
        QueuedIssue queued = CreateFreshQueued();

        // Act
        int tierRank = queued.TierRank;

        // Assert
        tierRank.ShouldBe(2);
    }
}
