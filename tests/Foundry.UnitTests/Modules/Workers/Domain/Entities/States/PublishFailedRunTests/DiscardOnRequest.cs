using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishFailedRunTests;

public sealed class DiscardOnRequest
{
    [Fact]
    public void AnExhaustedPublish_IsDiscardedByAnOperator()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("refused", transient: false, PublishingRunFixture.ExitedAt);

        // Act
        FailedRun discarded = failed.DiscardOnRequest();

        // Assert
        discarded.Reason.CategoryToken.ShouldBe(FailureCategory.RunDiscardedToken);
    }

    [Fact]
    public void APublishStillRetrying_CannotBeDiscardedByAnOperator()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("timed out", transient: true, PublishingRunFixture.ExitedAt);

        // Act / Assert
        Should.Throw<InvalidOperationException>(() => failed.DiscardOnRequest());
    }
}
