using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.AwaitingInputIssueTests;

public sealed class ProvideInput
{
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AnsweredAt = PostedAt.AddMinutes(30);

    [Fact]
    public void WhenWaitingOnAPullRequest_QueuesAnInputProvidedRevisionOfIt()
    {
        // Arrange
        IssueBuilder builder = new IssueBuilder()
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/7");
        AwaitingInputIssue awaiting = builder.AwaitingInputPosted(PostedAt);

        // Act
        QueuedIssue queued = awaiting.ProvideInput("Use Postgres.", AnsweredAt);

        // Assert
        RevisionQueuedIssue revision = queued.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.InputProvided>(),
            () => revision.BranchName.ShouldBe("feat/1-test"),
            () => revision.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/7"),
            () => awaiting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionQueued>());
    }

    [Fact]
    public void WhenTheRunLeftCommitsWithoutAPullRequest_QueuesAContinuationWithAnEmptyFailureReason()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().WithBranchName("feat/1-test").AwaitingInputPosted(PostedAt, withPullRequest: false);

        // Act
        QueuedIssue queued = awaiting.ProvideInput("Use Postgres.", AnsweredAt);

        // Assert
        ContinuationQueuedIssue continuation = queued.ShouldBeOfType<ContinuationQueuedIssue>();
        continuation.ShouldSatisfyAllConditions(
            () => continuation.BranchName.ShouldBe("feat/1-test"),
            () => continuation.FailureReason.ShouldBeEmpty());
    }

    [Fact]
    public void WhenTheRunPushedNothing_QueuesAFreshRun()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputWithoutPullRequest(branchHasCommits: false);
        awaiting.RecordQuestionPosted(1, PostedAt, commentUrl: null);

        // Act
        QueuedIssue queued = awaiting.ProvideInput("Use Postgres.", AnsweredAt);

        // Assert
        queued.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public void WhenInputArrives_ClosesTheOpenClarificationRoundAndCarriesTheLog()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);

        // Act
        QueuedIssue queued = awaiting.ProvideInput("Use Postgres.", AnsweredAt);

        // Assert
        Clarification round = queued.Clarifications.ShouldHaveSingleItem();
        round.ShouldSatisfyAllConditions(
            () => round.Questions.ShouldBe(IssueBuilder.DefaultQuestions),
            () => round.Input.ShouldBe("Use Postgres."),
            () => round.AnsweredAt.ShouldBe(AnsweredAt),
            () => queued.OpenClarification.ShouldBeNull());
    }

    [Fact]
    public void WhenTheQuestionIsNotPosted_Throws()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Act + Assert
        Should.Throw<InvalidOperationException>(() => awaiting.ProvideInput("Use Postgres.", AnsweredAt));
    }

    [Fact]
    public void WhenTheResumedRunIsClaimed_TheLogIsCarriedIntoTheRun()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);
        RevisionQueuedIssue queued = (RevisionQueuedIssue)awaiting.ProvideInput("Use Postgres.", AnsweredAt);

        // Act
        RevisionInProgressIssue inProgress = queued.Claim(Foundry.Modules.Workers.Contracts.WorkerRunId.New());

        // Assert
        inProgress.Clarifications.ShouldBe(queued.Clarifications);
    }
}
