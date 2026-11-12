using Foundry.Modules.Workers.Domain.Entities.States;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

public sealed class FailPublish
{
    private static readonly DateTimeOffset FailedAt = PublishingRunFixture.ExitedAt.AddMinutes(1);

    [Fact]
    public void ATransientFailure_IsRetriedAfterTheFirstDelay()
    {
        // Act
        PublishFailedRun failed = PublishingRunFixture.Publishing().FailPublish("provider down", transient: true, FailedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.FailedPublishAttempts.ShouldBe(1),
            () => failed.NextPublishAttemptAt.ShouldBe(FailedAt + PublishRetryPolicy.RetryDelays[0]),
            () => failed.IsExhausted.ShouldBeFalse(),
            () => failed.PublishFailureReason.ShouldBe("provider down"));
    }

    [Fact]
    public void APermanentFailure_IsExhaustedAtOnce()
    {
        // Act
        PublishFailedRun failed = PublishingRunFixture.Publishing().FailPublish("invalid bundle", transient: false, FailedAt);

        // Assert
        failed.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public void TheLastAllowedAttempt_ExhaustsTheRetries()
    {
        // Arrange
        PublishingRun publishing = PublishingRunFixture.Publishing();
        DateTimeOffset now = FailedAt;
        PublishFailedRun failed = publishing.FailPublish("down", transient: true, now);

        // Act
        while (!failed.IsExhausted)
        {
            now = failed.NextPublishAttemptAt!.Value;
            failed = failed.RetryPublish(now).FailPublish("down", transient: true, now);
        }

        // Assert
        failed.FailedPublishAttempts.ShouldBe(PublishRetryPolicy.MaxAttempts);
    }

    [Fact]
    public void AnOperatorRetry_StartsAFreshSetOfAttempts()
    {
        // Arrange
        PublishFailedRun exhausted = PublishingRunFixture.Publishing().FailPublish("refused", transient: false, FailedAt);

        // Act
        PublishingRun retried = exhausted.RequestRetry();

        // Assert
        retried.ShouldSatisfyAllConditions(
            () => retried.FailedPublishAttempts.ShouldBe(0),
            () => retried.Exit.ShouldBe(exhausted.Exit),
            () => retried.PullRequestText.ShouldBe(exhausted.PullRequestText));
    }

    [Fact]
    public void AnOperatorRetry_WhileStillRetrying_IsRefused()
    {
        // Arrange
        PublishFailedRun retrying = PublishingRunFixture.Publishing().FailPublish("down", transient: true, FailedAt);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => retrying.RequestRetry());
    }
}
