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
/// A person's review feedback stops Foundry merging the pull request on its own (issue #917): a review-feedback
/// revision sets the flag, every later transition carries it, and no other revision trigger sets it.
/// </summary>
public sealed class HasPersonFeedback
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ReviewComment> Comments =
    [
        new ReviewComment("Please rename this.", "src/a.cs", 3),
    ];

    private static readonly IReadOnlyList<FailingCheck> Failing =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
    ];

    private static IssueBuilder Builder() => new IssueBuilder()
        .WithVerifiedHeadSha(Head)
        .WithReviewComments(Comments)
        .WithFailingChecks(Failing);

    [Fact]
    public void WhenIssueNeverHadReviewFeedback_IsFalse()
    {
        // Act
        ChecksPendingIssue checksPending = Builder().ChecksPending();

        // Assert
        checksPending.HasPersonFeedback.ShouldBeFalse();
    }

    [Fact]
    public void WhenReviewIssueRevises_SetsIt()
    {
        // Act
        RevisionQueuedIssue revisionQueued = Builder().Review().Revise(Comments, Now);

        // Assert
        revisionQueued.HasPersonFeedback.ShouldBeTrue();
    }

    [Fact]
    public void WhenChecksPendingIssueRevises_SetsIt()
    {
        // Act
        RevisionQueuedIssue revisionQueued = Builder().ChecksPending().Revise(Comments, Now);

        // Assert
        revisionQueued.HasPersonFeedback.ShouldBeTrue();
    }

    [Fact]
    public void WhenMergePendingIssueRevises_SetsIt()
    {
        // Act
        RevisionQueuedIssue revisionQueued = Builder().MergePending().Revise(Comments, Now);

        // Assert
        revisionQueued.HasPersonFeedback.ShouldBeTrue();
    }

    public static TheoryData<string> CarryingPaths =>
    [
        "RevisionInProgress",
        "RebasePending",
        "ChecksPending",
        "Review",
        "FixChecksRevision",
        "RebaseConflictRevision",
        "RevisionFailedRetried",
        "PublishFailed",
        "PublishBlocked",
        "AwaitingInputAnswered",
        "BranchDivergedRetried",
        "ContinuableFailedRetried",
        "ContinuableFailedRetriedFresh",
        "Completed",
    ];

    [Theory]
    [MemberData(nameof(CarryingPaths))]
    public void WhenIssueMovesOnAfterReviewFeedback_CarriesIt(string path)
    {
        // Act
        Issue issue = FollowAfterFeedback(Builder().RevisionQueued(), path);

        // Assert
        issue.HasPersonFeedback.ShouldBeTrue();
    }

    [Fact]
    public void WhenFixChecksRevisionStarts_LeavesItFalse()
    {
        // Act
        RevisionQueuedIssue revisionQueued = Builder().ChecksPending().FixChecks(Failing, Now);

        // Assert
        revisionQueued.HasPersonFeedback.ShouldBeFalse();
    }

    [Fact]
    public void WhenRebaseConflictRevisionStarts_LeavesItFalse()
    {
        // Act
        RevisionQueuedIssue revisionQueued = Builder().RebasePending().ResolveConflict(Now);

        // Assert
        revisionQueued.HasPersonFeedback.ShouldBeFalse();
    }

    [Fact]
    public void WhenInputProvidedRevisionStarts_LeavesItFalse()
    {
        // Arrange
        RevisionInProgressIssue fixRun = Builder().ChecksPending().FixChecks(Failing, Now).Claim(WorkerRunId.New(), Now);

        // Act
        Issue resumed = AnswerQuestion(fixRun);

        // Assert
        resumed.ShouldBeOfType<RevisionQueuedIssue>().HasPersonFeedback.ShouldBeFalse();
    }

    private static Issue FollowAfterFeedback(RevisionQueuedIssue revisionQueued, string path)
    {
        RevisionInProgressIssue inProgress = revisionQueued.Claim(WorkerRunId.New(), Now);
        return path switch
        {
            "RevisionInProgress" => inProgress,
            "RebasePending" => inProgress.MarkRebasePending(Now, Now),
            "ChecksPending" => ToChecksPending(inProgress),
            "Review" => ToChecksPending(inProgress).ToReview(new ChecksOutcome.Passed(), Head, Now),
            "FixChecksRevision" => ToChecksPending(inProgress).FixChecks(Failing, Now),
            "RebaseConflictRevision" => inProgress.MarkRebasePending(Now, Now).ResolveConflict(Now),
            "RevisionFailedRetried" => inProgress.MarkFailed("boom", FailureCategory.NonZeroExit, Now).Retry(Now),
            "PublishFailed" => inProgress.MarkPublishFailed("push refused", Now),
            "PublishBlocked" => inProgress.MarkPublishBlocked(Now),
            "AwaitingInputAnswered" => AnswerQuestion(inProgress),
            "BranchDivergedRetried" => inProgress
                .MarkBranchDiverged("feat/1-test-run-1", BranchDivergenceReason.BranchMoved, Now)
                .Retry(Now),
            "ContinuableFailedRetried" => ToReview(inProgress).Fail("closed", FailureCategory.PrClosed, Now).Retry(Now),
            "ContinuableFailedRetriedFresh" =>
                ToReview(inProgress).Fail("closed", FailureCategory.PrClosed, Now).RetryFresh(Now),
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
