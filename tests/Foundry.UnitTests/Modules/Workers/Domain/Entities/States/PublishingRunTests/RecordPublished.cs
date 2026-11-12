using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

public sealed class RecordPublished
{
    private static readonly CommitSha Head = CommitSha.From("3333333333333333333333333333333333333333");

    [Fact]
    public void APush_IsKeptAsThePublishFacts()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();

        // Act
        run.RecordPublished(new PublishResult.Published(Head, 3, ["feat: a", "fix: b"], []), PublishingRunFixture.ExitedAt);

        // Assert
        run.ShouldSatisfyAllConditions(
            () => run.HasPublished.ShouldBeTrue(),
            () => run.PublishedHead.ShouldBe(Head),
            () => run.PublishedBranchCommitCount.ShouldBe(3),
            () => run.PublishedCommitSubjects.ShouldBe(["feat: a", "fix: b"]),
            () => run.DivergedSideBranch.ShouldBeNull());
    }

    [Fact]
    public void ADivergedBranch_KeepsTheSideBranch()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        BranchName side = BranchName.From("feat/42-add-retry-run-1");

        // Act
        run.RecordPublished(new PublishResult.Diverged(PublishingRunFixture.Branch, side, Head, []), PublishingRunFixture.ExitedAt);

        // Assert
        run.ShouldSatisfyAllConditions(
            () => run.DivergedSideBranch.ShouldBe(side),
            () => run.PublishedHead.ShouldBeNull());
    }

    [Fact]
    public void AFailedPublish_IsNotRecordedHere()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();

        // Act & Assert
        Should.Throw<ArgumentException>(
            () => run.RecordPublished(new PublishResult.Failed("x", Transient: true), PublishingRunFixture.ExitedAt));
    }

    [Fact]
    public void Completing_CarriesThePublishFactsToTheCompletedRun()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        run.RecordPublished(new PublishResult.Published(Head, 2, [], []), PublishingRunFixture.ExitedAt);

        // Act
        CompletedRun completed = run.Complete(0, PublishingRunFixture.Branch, PullRequestUrl.From("https://github.com/o/r/pull/7"));

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.PublishedHead.ShouldBe(Head),
            () => completed.PublishedBranchCommitCount.ShouldBe(2),
            () => completed.VerdictStatus.ShouldBe(RunVerdictStatus.Done),
            () => completed.Base.ShouldBe(PublishingRunFixture.Base));
    }
}
