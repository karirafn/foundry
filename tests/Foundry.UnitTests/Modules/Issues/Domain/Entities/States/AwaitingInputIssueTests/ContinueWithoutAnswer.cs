using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.AwaitingInputIssueTests;

public sealed class ContinueWithoutAnswer
{
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = PostedAt.AddDays(4);

    private static readonly ClarificationInput Unanswered = new([new InputPart.ContinuedWithoutAnswer()]);

    [Fact]
    public void WhenWaitingOnAPullRequest_QueuesARevisionThatRefusesToAskAgain()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder()
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/7")
            .AwaitingInputPosted(PostedAt);

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.ContinuedWithoutAnswer.ShouldBeTrue(),
            () => revision.Context.ContinuedWithoutAnswer.ShouldBeTrue(),
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.InputProvided>(),
            () => awaiting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionQueued>());
    }

    [Fact]
    public void WhenTheRunLeftCommitsWithoutAPullRequest_QueuesAContinuationThatRefusesToAskAgain()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt, withPullRequest: false);

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        ContinuationQueuedIssue continuation = next.ShouldBeOfType<ContinuationQueuedIssue>();
        continuation.Context.ContinuedWithoutAnswer.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheRunPushedNothing_QueuesAFreshRunThatRefusesToAskAgain()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputWithoutPullRequest(branchHasCommits: false);
        awaiting.RecordQuestionPosted(1, PostedAt, commentUrl: null);

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        FreshQueuedIssue fresh = next.ShouldBeOfType<FreshQueuedIssue>();
        fresh.Context.ContinuedWithoutAnswer.ShouldBeTrue();
    }

    [Fact]
    public void WhenContinued_ClosesTheOpenRoundAsContinuedWithoutAnswerAtTheGivenTime()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(PostedAt);

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        Clarification.Asked round = next.Clarifications.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>();
        round.ShouldSatisfyAllConditions(
            () => round.Input.ShouldBe(Unanswered),
            () => round.AnsweredAt.ShouldBe(Now),
            () => next.OpenClarification.ShouldBeNull());
    }

    [Fact]
    public void WhenTheQuestionIsStillPending_ClosesTheRoundAsUnanswered()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        next.Clarifications.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Input.ShouldBe(Unanswered);
    }

    [Fact]
    public void WhenThePostFailed_ClosesTheRoundAsUnanswered()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();
        awaiting.RecordQuestionPostFailed("403", transient: false, PostedAt);

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        next.Clarifications.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Input.ShouldBe(Unanswered);
    }

    [Fact]
    public void WhenTriageAsked_ReleasesTheIssueThroughTheDependencyCheckWithoutTriaging()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputFromTriage(IssueKind.Feature);
        awaiting.RecordQuestionPosted(1, PostedAt, commentUrl: null);

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        FreshQueuedIssue fresh = next.ShouldBeOfType<FreshQueuedIssue>();
        fresh.ShouldSatisfyAllConditions(
            () => fresh.Clarifications.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Input.ShouldBe(Unanswered),
            () => fresh.Context.ContinuedWithoutAnswer.ShouldBeFalse(),
            () => awaiting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>());
    }

    [Fact]
    public void WhenTriageAskedAndTheIssueHasOpenBlockers_BlocksIt()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().WithBlockedBy([5]).AwaitingInputFromTriage(IssueKind.Feature);

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        next.ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([5]);
    }

    [Fact]
    public void WhenTheContinuedRunIsRetriedAfterIt_TheRetriedRunMayAskAgain()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputWithoutPullRequest(branchHasCommits: false);
        FreshQueuedIssue continued = (FreshQueuedIssue)awaiting.ContinueWithoutAnswer(Now);
        FailedIssue failed = continued.Claim(Foundry.Modules.Workers.Contracts.WorkerRunId.New(), Now)
            .MarkFailed("boom", Now, Foundry.Modules.Workers.Contracts.FailureCategory.NonZeroExit);

        // Act
        Issue retried = failed.Retry(Now);

        // Assert
        retried.ShouldBeAssignableTo<QueuedIssue>().ShouldNotBeNull().Context.ContinuedWithoutAnswer.ShouldBeFalse();
    }
}
