using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// A person's pull request feedback is recorded on the clarification log as a requested round when Foundry consumes it,
/// and every later transition carries the round.
/// </summary>
public sealed class PersonFeedbackRound
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset NewestCommentAt = Now.AddMinutes(-5);

    private static readonly IReadOnlyList<ReviewComment> Comments =
    [
        new ReviewComment("Date should be a date picker.", "src/Form.cs", 12),
        new ReviewComment("Looks good otherwise."),
    ];

    private static readonly IReadOnlyList<FailingCheck> Failing =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
    ];

    private static readonly Clarification.Requested Expected = Clarification.Request(Comments, 3, NewestCommentAt);

    private static IssueBuilder Builder() => new IssueBuilder()
        .WithVerifiedHeadSha(Head)
        .WithReviewComments(Comments)
        .WithFailingChecks(Failing);

    public static TheoryData<string> FeedbackSources => ["Review", "ChecksPending", "MergePending"];

    [Theory]
    [MemberData(nameof(FeedbackSources))]
    public void WhenFeedbackIsConsumed_AppendsOneRequestedRoundWithTheBatch(string source)
    {
        // Act
        RevisionQueuedIssue revisionQueued = Revise(source, NewestCommentAt);

        // Assert
        revisionQueued.Clarifications.ShouldBe([Expected]);
    }

    [Theory]
    [MemberData(nameof(FeedbackSources))]
    public void WhenNoConsumedCommentHasATime_TheRoundIsReceivedAtTheTransition(string source)
    {
        // Act
        RevisionQueuedIssue revisionQueued = Revise(source, newestCommentAt: null);

        // Assert
        revisionQueued.Clarifications.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Requested>().ReceivedAt.ShouldBe(Now);
    }

    [Fact]
    public void ARequestedRound_IsNeverTheOpenClarification()
    {
        // Act
        RevisionQueuedIssue revisionQueued = Revise("Review", NewestCommentAt);

        // Assert
        revisionQueued.OpenClarification.ShouldBeNull();
    }

    [Fact]
    public void WhenFeedbackIsConsumedTwice_EachBatchIsItsOwnRoundNewestLast()
    {
        // Arrange
        RevisionInProgressIssue inProgress = Revise("Review", NewestCommentAt).Claim(WorkerRunId.New(), Now);
        IReadOnlyList<ReviewComment> second = [new ReviewComment("Use the ISO format.")];

        // Act
        RevisionQueuedIssue again = ToReview(inProgress).Revise(second, Now.AddHours(1), 0, Now.AddMinutes(30));

        // Assert
        again.Clarifications.ShouldBe([Expected, Clarification.Request(second, 0, Now.AddMinutes(30))]);
    }

    [Fact]
    public void WhenChecksFixRevisionStarts_AppendsNoRound()
    {
        // Act
        RevisionQueuedIssue revisionQueued = Builder().ChecksPending().FixChecks(Failing, Now);

        // Assert
        revisionQueued.Clarifications.ShouldBeEmpty();
    }

    [Fact]
    public void WhenRebaseConflictRevisionStarts_AppendsNoRound()
    {
        // Act
        RevisionQueuedIssue revisionQueued = Builder().RebasePending().ResolveConflict(Now);

        // Assert
        revisionQueued.Clarifications.ShouldBeEmpty();
    }

    public static TheoryData<string> CarryingPaths =>
    [
        "AwaitingInputAnswered",
        "RevisionFailedRetried",
        "ChecksFixRevision",
        "Completed",
    ];

    [Theory]
    [MemberData(nameof(CarryingPaths))]
    public void WhenTheIssueMovesOnAfterFeedback_KeepsTheRequestedRoundFirst(string path)
    {
        // Act
        Issue issue = FollowAfterFeedback(Revise("Review", NewestCommentAt), path);

        // Assert
        issue.Clarifications[0].ShouldBe(Expected);
    }

    [Fact]
    public void WhenARunResumesAfterAQuestion_TheRevisionCarriesTheRequestedRoundAndTheAnsweredRound()
    {
        // Act
        Issue resumed = FollowAfterFeedback(Revise("Review", NewestCommentAt), "AwaitingInputAnswered");

        // Assert
        resumed.ShouldSatisfyAllConditions(
            () => resumed.ShouldBeOfType<RevisionQueuedIssue>().Trigger.ShouldBeOfType<RevisionTrigger.InputProvided>(),
            () => resumed.Clarifications.Count.ShouldBe(2),
            () => resumed.Clarifications[1].ShouldBeOfType<Clarification.Asked>().IsOpen.ShouldBeFalse());
    }

    private static RevisionQueuedIssue Revise(string source, DateTimeOffset? newestCommentAt) => source switch
    {
        "Review" => Builder().Review().Revise(Comments, Now, 3, newestCommentAt),
        "ChecksPending" => Builder().ChecksPending().Revise(Comments, Now, 3, newestCommentAt),
        "MergePending" => Builder().MergePending().Revise(Comments, Now, 3, newestCommentAt),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    private static Issue FollowAfterFeedback(RevisionQueuedIssue revisionQueued, string path)
    {
        RevisionInProgressIssue inProgress = revisionQueued.Claim(WorkerRunId.New(), Now);
        return path switch
        {
            "AwaitingInputAnswered" => AnswerQuestion(inProgress),
            "RevisionFailedRetried" => inProgress.MarkFailed("boom", FailureCategory.NonZeroExit, Now).Retry(Now),
            "ChecksFixRevision" => ToChecksPending(inProgress).FixChecks(Failing, Now),
            "Completed" => ToReview(inProgress).Complete(Now),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        };
    }

    private static ChecksPendingIssue ToChecksPending(RevisionInProgressIssue inProgress) =>
        inProgress.MarkRebasePending(Now, Now).MarkChecksPending(new RebaseOutcome.UpToDate(), Now);

    private static ReviewIssue ToReview(RevisionInProgressIssue inProgress) =>
        ToChecksPending(inProgress).ToReview(new ChecksOutcome.Passed(), Head, Now);

    private static Issue AnswerQuestion(RevisionInProgressIssue inProgress)
    {
        AwaitingInputIssue awaiting = inProgress.MarkAwaitingInput(
            [new InputQuestion.Legacy("Which name?", "Two candidates.", null)],
            Now,
            Now);
        awaiting.RecordQuestionPosted(1, Now, null);
        return awaiting.ProvideInput(ClarificationInput.FromComments("Use the first."), Now.AddMinutes(1), Now.AddMinutes(1));
    }
}
