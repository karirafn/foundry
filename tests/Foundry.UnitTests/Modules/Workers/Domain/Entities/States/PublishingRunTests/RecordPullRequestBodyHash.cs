using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

public sealed class RecordPullRequestBodyHash
{
    private static readonly PullRequestBodyHash BodyHash = PullRequestBodyHash.Hash("Closes #42");

    [Fact]
    public void TheHash_IsKeptOnTheRun()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();

        // Act
        run.RecordPullRequestBodyHash(BodyHash);

        // Assert
        run.PullRequestBodyHash.ShouldBe(BodyHash);
    }

    [Fact]
    public void TheHash_IsCarriedThroughAFailedPublishAndItsRetry()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        run.RecordPullRequestBodyHash(BodyHash);

        // Act
        PublishingRun retried = run
            .FailPublish("provider down", transient: true, PublishingRunFixture.ExitedAt)
            .RetryPublish(PublishingRunFixture.ExitedAt.AddMinutes(5));

        // Assert
        retried.PullRequestBodyHash.ShouldBe(BodyHash);
    }

    [Fact]
    public void TheHash_IsCarriedIntoTheCompletedRun()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        run.RecordPullRequestBodyHash(BodyHash);

        // Act
        CompletedRun completed = run.Complete(0, PublishingRunFixture.Branch, pullRequestUrl: null);

        // Assert
        completed.PullRequestBodyHash.ShouldBe(BodyHash);
    }

    [Fact]
    public void TheHash_IsCarriedIntoTheFailedRun()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        run.RecordPullRequestBodyHash(BodyHash);

        // Act
        FailedRun failed = run.Fail(new FailureReason.TimedOut(), PublishingRunFixture.Branch);

        // Assert
        failed.PullRequestBodyHash.ShouldBe(BodyHash);
    }
}
