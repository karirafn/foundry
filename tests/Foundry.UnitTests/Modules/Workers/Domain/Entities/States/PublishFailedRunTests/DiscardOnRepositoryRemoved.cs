using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishFailedRunTests;

public sealed class DiscardOnRepositoryRemoved
{
    [Fact]
    public void AnExhaustedPublish_IsDiscardedAsRepositoryRemoved()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("refused", transient: false, PublishingRunFixture.ExitedAt);

        // Act
        FailedRun discarded = failed.DiscardOnRepositoryRemoved();

        // Assert
        discarded.ShouldSatisfyAllConditions(
            () => discarded.Reason.ShouldBeOfType<FailureReason.RepositoryRemoved>(),
            () => discarded.Reason.CategoryToken.ShouldBe(FailureCategory.RepositoryRemovedToken),
            () => discarded.BranchName.ShouldBe(PublishingRunFixture.Branch));
    }

    [Fact]
    public void APublishStillRetrying_IsDiscardedAsRepositoryRemoved()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("timed out", transient: true, PublishingRunFixture.ExitedAt);

        // Act
        FailedRun discarded = failed.DiscardOnRepositoryRemoved();

        // Assert
        discarded.Reason.CategoryToken.ShouldBe(FailureCategory.RepositoryRemovedToken);
    }
}
