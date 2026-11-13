using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishBlockedRunTests;

public sealed class Discard
{
    private static PublishBlockedRun Blocked() =>
        PublishingRunFixture.Publishing().BlockPublish(
            [new SecretFinding("a.env", 1, CommitSha.From("3333333333333333333333333333333333333333"), "github-pat")],
            PublishingRunFixture.ExitedAt);

    [Fact]
    public void AnOperatorDiscard_FailsTheRunAsDiscardedOnItsBranch()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked();

        // Act
        FailedRun discarded = blocked.DiscardOnRequest();

        // Assert
        discarded.ShouldSatisfyAllConditions(
            () => discarded.Reason.ShouldBeOfType<FailureReason.RunDiscarded>(),
            () => discarded.Reason.CategoryToken.ShouldBe(FailureCategory.RunDiscardedToken),
            () => discarded.BranchName.ShouldBe(PublishingRunFixture.Branch),
            () => blocked.DomainEvents.ShouldHaveSingleItem());
    }

    [Fact]
    public void TheTimeToLive_DiscardsTheRunAsExpired()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked();

        // Act
        FailedRun discarded = blocked.Discard();

        // Assert
        discarded.Reason.CategoryToken.ShouldBe(FailureCategory.PublishExpiredToken);
    }

    [Fact]
    public void AnIssueClosedOnTheProvider_DiscardsTheRunAsIssueClosed()
    {
        // Arrange
        PublishBlockedRun blocked = Blocked();

        // Act
        FailedRun discarded = blocked.DiscardOnIssueClosed();

        // Assert
        discarded.ShouldSatisfyAllConditions(
            () => discarded.Reason.ShouldBeOfType<FailureReason.IssueClosed>(),
            () => discarded.Reason.CategoryToken.ShouldBe(FailureCategory.IssueClosedToken),
            () => discarded.BranchName.ShouldBe(PublishingRunFixture.Branch),
            () => blocked.DomainEvents.ShouldHaveSingleItem());
    }
}
