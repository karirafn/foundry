using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

public sealed class BlockPublish
{
    private static readonly SecretFinding Finding = new(
        "src/appsettings.json",
        14,
        CommitSha.From("3333333333333333333333333333333333333333"),
        "aws-access-token");

    [Fact]
    public void ADetectedSecret_BlocksThePublishKeepingTheFindingsAndTheUnpublishedWork()
    {
        // Arrange
        PublishingRun publishing = PublishingRunFixture.Publishing();
        DateTimeOffset blockedAt = PublishingRunFixture.ExitedAt.AddMinutes(1);

        // Act
        PublishBlockedRun blocked = publishing.BlockPublish([Finding], blockedAt);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.Id.ShouldBe(publishing.Id),
            () => blocked.SecretFindings.ShouldBe([Finding]),
            () => blocked.PublishBlockedAt.ShouldBe(blockedAt),
            () => blocked.BranchName.ShouldBe(PublishingRunFixture.Branch),
            () => blocked.Base.ShouldBe(PublishingRunFixture.Base),
            () => blocked.PublishedHead.ShouldBeNull(),
            () => blocked.DiscardsAt.ShouldBe(PublishingRunFixture.ExitedAt + PublishRetryPolicy.HandOffTimeToLive));
    }

    [Fact]
    public void ABlockWithoutFindings_IsRejected()
    {
        // Arrange
        PublishingRun publishing = PublishingRunFixture.Publishing();

        // Act / Assert
        Should.Throw<ArgumentException>(() => publishing.BlockPublish([], PublishingRunFixture.ExitedAt));
    }
}
