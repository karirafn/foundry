using Foundry.Modules.Workers.Contracts;
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
        run.RecordPublished(new PublishResult.Published(Head, 3, ["feat: a", "fix: b"], [], []), PublishingRunFixture.ExitedAt);

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
        run.RecordPublished(
            new PublishResult.Diverged(PublishingRunFixture.Branch, side, Head, BranchDivergenceReason.BranchMoved, [], []),
            PublishingRunFixture.ExitedAt);

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
            () => run.RecordPublished(new PublishResult.Failed("x", PublishFailureKind.Transient), PublishingRunFixture.ExitedAt));
    }

    [Fact]
    public void Completing_CarriesThePublishFactsToTheCompletedRun()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        run.RecordPublished(new PublishResult.Published(Head, 2, [], [], []), PublishingRunFixture.ExitedAt);

        // Act
        CompletedRun completed = run.Complete(0, PublishingRunFixture.Branch, PullRequestUrl.From("https://github.com/o/r/pull/7"));

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.PublishedHead.ShouldBe(Head),
            () => completed.PublishedBranchCommitCount.ShouldBe(2),
            () => completed.VerdictStatus.ShouldBe(RunVerdictStatus.Done),
            () => completed.Base.ShouldBe(PublishingRunFixture.Base));
    }

    [Fact]
    public void ARefusedForcePush_KeepsTheSideBranchWithItsReason()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        BranchName side = BranchName.From("feat/42-add-retry-run-1");

        // Act
        run.RecordPublished(
            new PublishResult.Diverged(PublishingRunFixture.Branch, side, Head, BranchDivergenceReason.ForcePushProtected, [], []),
            PublishingRunFixture.ExitedAt);

        // Assert
        run.Divergence.ShouldBe(new BranchDivergence(side, BranchDivergenceReason.ForcePushProtected));
    }

    [Fact]
    public void APush_HasNoDivergence()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();

        // Act
        run.RecordPublished(new PublishResult.Published(Head, 1, [], [], []), PublishingRunFixture.ExitedAt);

        // Assert
        run.Divergence.ShouldBeNull();
    }

    [Fact]
    public void Completing_CarriesTheDivergenceReasonToTheCompletedRun()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        BranchName side = BranchName.From("feat/42-add-retry-run-1");
        run.RecordPublished(
            new PublishResult.Diverged(PublishingRunFixture.Branch, side, Head, BranchDivergenceReason.ForcePushProtected, [], []),
            PublishingRunFixture.ExitedAt);

        // Act
        CompletedRun completed = run.Complete(0, PublishingRunFixture.Branch, pullRequestUrl: null);

        // Assert
        completed.Divergence.ShouldBe(new BranchDivergence(side, BranchDivergenceReason.ForcePushProtected));
    }

    private static readonly SecretFinding Generic =
        new("src/store.ts", 3, CommitSha.From("4444444444444444444444444444444444444444"), "generic-api-key");

    [Fact]
    public void APush_KeepsItsGenericFindings()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();

        // Act
        run.RecordPublished(new PublishResult.Published(Head, 1, [], [], [Generic]), PublishingRunFixture.ExitedAt);

        // Assert
        run.GenericFindings.ShouldBe([Generic]);
    }

    [Fact]
    public void ADivergedBranch_KeepsItsGenericFindings()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        BranchName side = BranchName.From("feat/42-add-retry-run-1");

        // Act
        run.RecordPublished(
            new PublishResult.Diverged(PublishingRunFixture.Branch, side, Head, BranchDivergenceReason.BranchMoved, [], [Generic]),
            PublishingRunFixture.ExitedAt);

        // Assert
        run.GenericFindings.ShouldBe([Generic]);
    }

    [Fact]
    public void GenericFindings_NeverEnterTheConfirmedSet()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();

        // Act
        run.RecordPublished(new PublishResult.Published(Head, 1, [], [], [Generic]), PublishingRunFixture.ExitedAt);

        // Assert
        run.ShouldSatisfyAllConditions(
            () => run.ConfirmedFindings.ShouldBeEmpty(),
            () => run.FalsePositiveConfirmation.ShouldBeNull());
    }

    [Fact]
    public void Completing_CarriesTheGenericFindingsToTheCompletedRun()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        run.RecordPublished(new PublishResult.Published(Head, 1, [], [], [Generic]), PublishingRunFixture.ExitedAt);

        // Act
        CompletedRun completed = run.Complete(0, PublishingRunFixture.Branch, pullRequestUrl: null);

        // Assert
        completed.GenericFindings.ShouldBe([Generic]);
    }

    [Fact]
    public void Failing_CarriesTheGenericFindingsToTheFailedRun()
    {
        // Arrange
        PublishingRun run = PublishingRunFixture.Publishing();
        run.RecordPublished(new PublishResult.Published(Head, 1, [], [], [Generic]), PublishingRunFixture.ExitedAt);

        // Act
        FailedRun failed = run.FailPublish("opening the pull request failed", transient: false, PublishingRunFixture.ExitedAt)
            .DiscardOnRequest();

        // Assert
        failed.GenericFindings.ShouldBe([Generic]);
    }

    [Fact]
    public void ARetryThatPublishes_ReplacesTheGenericFindingsWithItsOwn()
    {
        // Arrange
        SecretFinding later = new("src/other.ts", 9, CommitSha.From("5555555555555555555555555555555555555555"), "generic-api-key");
        PublishingRun run = PublishingRunFixture.Publishing();
        run.RecordPublished(new PublishResult.Published(Head, 1, [], [], [Generic]), PublishingRunFixture.ExitedAt);
        PublishingRun retried = run.FailPublish("opening the pull request failed", transient: false, PublishingRunFixture.ExitedAt)
            .RequestRetry();
        retried.GenericFindings.ShouldBe([Generic]);

        // Act
        retried.RecordPublished(new PublishResult.Published(Head, 1, [], [], [later]), PublishingRunFixture.ExitedAt);

        // Assert
        retried.GenericFindings.ShouldBe([later]);
    }
}
