using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishFailedRunTests;

public sealed class Discard
{
    [Fact]
    public void AnUnpublishedRun_IsDiscardedAfterTheHandOffTimeToLive()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("refused", transient: false, PublishingRunFixture.ExitedAt);

        // Act
        FailedRun discarded = failed.Discard();

        // Assert
        discarded.ShouldSatisfyAllConditions(
            () => failed.DiscardsAt.ShouldBe(PublishingRunFixture.ExitedAt + TimeSpan.FromDays(7)),
            () => discarded.Reason.CategoryToken.ShouldBe(FailureCategory.PublishExpiredToken),
            () => discarded.BranchName.ShouldBe(PublishingRunFixture.Branch),
            () => failed.DomainEvents.ShouldHaveSingleItem());
    }

    [Fact]
    public void WhenDiscarded_CarriesTheRunBaseForward()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("refused", transient: false, PublishingRunFixture.ExitedAt);

        // Act
        FailedRun discarded = failed.Discard();

        // Assert
        discarded.Base.ShouldBe(failed.Base);
    }
}
