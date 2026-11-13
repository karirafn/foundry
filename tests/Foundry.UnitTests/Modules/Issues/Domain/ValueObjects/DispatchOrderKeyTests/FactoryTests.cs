using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchOrderKeyTests;

public sealed class FactoryTests
{
    [Fact]
    public void WhenFreshQueued_BuildsKeyWithTierRankThree()
    {
        // Arrange
        int issueNumber = 42;
        FreshQueuedIssue freshQueued = new IssueBuilder().WithIssueNumber(issueNumber).FreshQueued();
        int position = 3;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(freshQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(3),
            () => key.Position.ShouldBe(3),
            () => key.IssueNumber.ShouldBe(42),
            () => key.Id.ShouldBe(freshQueued.Id));
    }

    [Fact]
    public void WhenRevisionQueued_BuildsKeyWithTierRankZero()
    {
        // Arrange
        int issueNumber = 42;
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithIssueNumber(issueNumber)
            .WithReviewComments([new ReviewComment("Please fix the formatting.")])
            .RevisionQueued();
        int position = 1;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(revisionQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(0),
            () => key.Position.ShouldBe(1),
            () => key.IssueNumber.ShouldBe(42),
            () => key.Id.ShouldBe(revisionQueued.Id));
    }

    [Fact]
    public void WhenContinuationQueued_BuildsKeyWithTierRankOne()
    {
        // Arrange
        int issueNumber = 42;
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithIssueNumber(issueNumber)
            .ContinuableFailed()
            .Retry(DateTimeOffset.UtcNow);
        int position = 2;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(continuationQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(1),
            () => key.Position.ShouldBe(2),
            () => key.IssueNumber.ShouldBe(42),
            () => key.Id.ShouldBe(continuationQueued.Id));
    }

    [Fact]
    public void WhenLowerNumberDetectedLater_SortsBeforeHigherNumberDetectedEarlier()
    {
        // Arrange — #12 detected after #50 in the same repository and tier
        DateTimeOffset baseTime = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        FreshQueuedIssue issue50 = new IssueBuilder()
            .WithIssueNumber(50)
            .WithDetectedAt(baseTime)
            .FreshQueued();
        FreshQueuedIssue issue12 = new IssueBuilder()
            .WithIssueNumber(12)
            .WithDetectedAt(baseTime.AddHours(1))
            .FreshQueued();

        // Act
        DispatchOrderKey key12 = DispatchOrderKey.For(issue12, position: 0);
        DispatchOrderKey key50 = DispatchOrderKey.For(issue50, position: 0);

        // Assert
        (key12 < key50).ShouldBeTrue();
    }
}
