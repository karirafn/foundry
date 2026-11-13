using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishBlockedRunTests;

public sealed class ConfirmFalsePositives
{
    private static readonly SecretFinding StorageKey = new(
        "src/store.ts",
        3,
        CommitSha.From("3333333333333333333333333333333333333333"),
        "generic-api-key");

    private static readonly SecretFinding FixtureUuid = new(
        "src/mix.service.spec.ts",
        41,
        CommitSha.From("4444444444444444444444444444444444444444"),
        "generic-api-key");

    private static readonly DateTimeOffset BlockedAt = PublishingRunFixture.ExitedAt.AddMinutes(1);

    private static readonly DateTimeOffset ConfirmedAt = PublishingRunFixture.ExitedAt.AddHours(2);

    [Fact]
    public void Confirming_PublishesTheSameRunCarryingTheFindingsAndTheTime()
    {
        // Arrange
        PublishBlockedRun blocked = PublishingRunFixture.Publishing().BlockPublish([StorageKey, FixtureUuid], BlockedAt);

        // Act
        PublishingRun publishing = blocked.ConfirmFalsePositives(ConfirmedAt);

        // Assert
        publishing.ShouldSatisfyAllConditions(
            () => publishing.Id.ShouldBe(blocked.Id),
            () => publishing.ConfirmedFindings.ShouldBe([StorageKey, FixtureUuid]),
            () => publishing.FalsePositivesConfirmedAt.ShouldBe(ConfirmedAt),
            () => publishing.HasPublished.ShouldBeFalse(),
            () => publishing.BranchName.ShouldBe(PublishingRunFixture.Branch),
            () => publishing.FailedPublishAttempts.ShouldBe(0));
    }

    [Fact]
    public void ConfirmingAgainAfterAReblock_KeepsTheEarlierFindingsAndRecordsTheLatestTime()
    {
        // Arrange
        PublishingRun firstPublish = PublishingRunFixture.Publishing()
            .BlockPublish([StorageKey], BlockedAt)
            .ConfirmFalsePositives(BlockedAt.AddMinutes(5));
        PublishBlockedRun reblocked = firstPublish.BlockPublish([FixtureUuid], BlockedAt.AddMinutes(6));

        // Act
        PublishingRun secondPublish = reblocked.ConfirmFalsePositives(ConfirmedAt);

        // Assert
        secondPublish.ShouldSatisfyAllConditions(
            () => secondPublish.ConfirmedFindings.ShouldBe([StorageKey, FixtureUuid]),
            () => secondPublish.FalsePositivesConfirmedAt.ShouldBe(ConfirmedAt));
    }

    [Fact]
    public void AReblockedRun_KeepsItsEarlierConfirmationWhileBlocked()
    {
        // Arrange
        PublishingRun confirmed = PublishingRunFixture.Publishing()
            .BlockPublish([StorageKey], BlockedAt)
            .ConfirmFalsePositives(ConfirmedAt);

        // Act
        PublishBlockedRun reblocked = confirmed.BlockPublish([FixtureUuid], ConfirmedAt.AddMinutes(1));

        // Assert
        reblocked.ShouldSatisfyAllConditions(
            () => reblocked.SecretFindings.ShouldBe([FixtureUuid]),
            () => reblocked.ConfirmedFindings.ShouldBe([StorageKey]),
            () => reblocked.FalsePositivesConfirmedAt.ShouldBe(ConfirmedAt));
    }

    [Fact]
    public void AFindingConfirmedTwice_IsKeptOnce()
    {
        // Arrange
        PublishingRun firstPublish = PublishingRunFixture.Publishing()
            .BlockPublish([StorageKey], BlockedAt)
            .ConfirmFalsePositives(BlockedAt.AddMinutes(5));
        PublishBlockedRun reblocked = firstPublish.BlockPublish([StorageKey, FixtureUuid], BlockedAt.AddMinutes(6));

        // Act
        PublishingRun secondPublish = reblocked.ConfirmFalsePositives(ConfirmedAt);

        // Assert
        secondPublish.ConfirmedFindings.ShouldBe([StorageKey, FixtureUuid]);
    }

    [Fact]
    public void AConfirmedRunsFailedPublish_KeepsTheConfirmationThroughItsRetry()
    {
        // Arrange
        PublishingRun confirmed = PublishingRunFixture.Publishing()
            .BlockPublish([StorageKey], BlockedAt)
            .ConfirmFalsePositives(ConfirmedAt);
        PublishFailedRun failed = confirmed.FailPublish("unreachable", transient: true, ConfirmedAt);

        // Act
        PublishingRun retried = failed.RetryPublish(ConfirmedAt.AddHours(1));

        // Assert
        retried.ShouldSatisfyAllConditions(
            () => failed.ConfirmedFindings.ShouldBe([StorageKey]),
            () => retried.ConfirmedFindings.ShouldBe([StorageKey]),
            () => retried.FalsePositivesConfirmedAt.ShouldBe(ConfirmedAt));
    }

    [Fact]
    public void AConfirmedRunThatCompletes_KeepsTheConfirmationOnItsHistory()
    {
        // Arrange
        PublishingRun confirmed = PublishingRunFixture.Publishing()
            .BlockPublish([StorageKey], BlockedAt)
            .ConfirmFalsePositives(ConfirmedAt);

        // Act
        CompletedRun completed = confirmed.Complete(0, PublishingRunFixture.Branch, pullRequestUrl: null);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.ConfirmedFindings.ShouldBe([StorageKey]),
            () => completed.FalsePositivesConfirmedAt.ShouldBe(ConfirmedAt));
    }

    [Fact]
    public void AConfirmedRunThatIsDiscarded_KeepsTheConfirmationOnItsHistory()
    {
        // Arrange
        PublishBlockedRun reblocked = PublishingRunFixture.Publishing()
            .BlockPublish([StorageKey], BlockedAt)
            .ConfirmFalsePositives(ConfirmedAt)
            .BlockPublish([FixtureUuid], ConfirmedAt.AddMinutes(1));

        // Act
        FailedRun discarded = reblocked.DiscardOnRequest();

        // Assert
        discarded.ShouldSatisfyAllConditions(
            () => discarded.ConfirmedFindings.ShouldBe([StorageKey]),
            () => discarded.FalsePositivesConfirmedAt.ShouldBe(ConfirmedAt));
    }

    [Fact]
    public void ARunNeverConfirmed_HasNoConfirmation()
    {
        // Act
        PublishBlockedRun blocked = PublishingRunFixture.Publishing().BlockPublish([StorageKey], BlockedAt);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.ConfirmedFindings.ShouldBeEmpty(),
            () => blocked.FalsePositivesConfirmedAt.ShouldBeNull());
    }
}
