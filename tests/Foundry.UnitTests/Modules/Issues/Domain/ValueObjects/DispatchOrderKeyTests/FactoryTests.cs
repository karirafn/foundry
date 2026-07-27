using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchOrderKeyTests;

public sealed class FactoryTests
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static DetectedIssue CreateDetected(MonitoredRepositoryId repositoryId, DateTimeOffset detectedAt) =>
        DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: detectedAt);

    private static QueuedIssue CreateFreshQueued(DateTimeOffset detectedAt)
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetected(repositoryId, detectedAt);
        return detected.Enqueue();
    }

    private static RevisionQueuedIssue CreateRevisionQueued(DateTimeOffset detectedAt)
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetected(repositoryId, detectedAt);
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);
        return review.Revise([new ReviewComment("Please fix the formatting.")]);
    }

    private static ContinuationQueuedIssue CreateContinuationQueued(DateTimeOffset detectedAt)
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetected(repositoryId, detectedAt);
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);
        return failed.Retry();
    }

    [Fact]
    public void WhenFreshQueued_BuildsKeyWithTierRankTwo()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        QueuedIssue freshQueued = CreateFreshQueued(detectedAt);
        int position = 3;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(freshQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(2),
            () => key.Position.ShouldBe(3),
            () => key.DetectedAt.ShouldBe(detectedAt),
            () => key.Id.ShouldBe(freshQueued.Id));
    }

    [Fact]
    public void WhenRevisionQueued_BuildsKeyWithTierRankZero()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        RevisionQueuedIssue revisionQueued = CreateRevisionQueued(detectedAt);
        int position = 1;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(revisionQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(0),
            () => key.Position.ShouldBe(1),
            () => key.DetectedAt.ShouldBe(detectedAt),
            () => key.Id.ShouldBe(revisionQueued.Id));
    }

    [Fact]
    public void WhenContinuationQueued_BuildsKeyWithTierRankOne()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueued(detectedAt);
        int position = 2;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(continuationQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(1),
            () => key.Position.ShouldBe(2),
            () => key.DetectedAt.ShouldBe(detectedAt),
            () => key.Id.ShouldBe(continuationQueued.Id));
    }

    [Fact]
    public void WhenNonQueuedIssue_ThrowsInvalidOperationException()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        QueuedIssue queued = CreateFreshQueued(detectedAt);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        int position = 1;

        // Act / Assert
        Should.Throw<InvalidOperationException>(() => DispatchOrderKey.For(inProgress, position));
    }
}
