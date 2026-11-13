using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishFailedRunTests;

public sealed class DiscardOnIssueClosed
{
    [Fact]
    public void AnExhaustedPublish_IsDiscardedAsIssueClosed()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("refused", transient: false, PublishingRunFixture.ExitedAt);

        // Act
        FailedRun discarded = failed.DiscardOnIssueClosed();

        // Assert
        discarded.ShouldSatisfyAllConditions(
            () => discarded.Reason.ShouldBeOfType<FailureReason.IssueClosed>(),
            () => discarded.Reason.CategoryToken.ShouldBe(FailureCategory.IssueClosedToken),
            () => discarded.BranchName.ShouldBe(PublishingRunFixture.Branch));
    }

    [Fact]
    public void APublishStillRetrying_IsDiscardedAsIssueClosed()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("timed out", transient: true, PublishingRunFixture.ExitedAt);

        // Act
        FailedRun discarded = failed.DiscardOnIssueClosed();

        // Assert
        discarded.Reason.CategoryToken.ShouldBe(FailureCategory.IssueClosedToken);
    }
}
