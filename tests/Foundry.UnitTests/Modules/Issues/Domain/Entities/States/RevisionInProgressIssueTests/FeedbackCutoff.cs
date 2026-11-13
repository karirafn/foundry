using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionInProgressIssueTests;

/// <summary>
/// The cutoff each of the two completion transitions carries forward, per revision trigger kind (issue #751).
/// A trigger that consumed no comments anchors to the run's start time, so a comment posted while the run was
/// working is still read by the next review pass.
/// </summary>
public sealed class FeedbackCutoff
{
    private static readonly DateTimeOffset NewestConsumedCommentAt = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStartedAt = new(2026, 6, 1, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CommentDuringRunAt = new(2026, 6, 1, 11, 30, 0, TimeSpan.Zero);

    public static TheoryData<string> Transitions =>
    [
        nameof(RevisionInProgressIssue.MarkRebasePending),
        nameof(RevisionInProgressIssue.MarkUnchangedRebasePending),
    ];

    [Theory]
    [MemberData(nameof(Transitions))]
    public void WhenFailedChecksTrigger_CutoffIsRunStartedAt(string transition)
    {
        // Arrange
        RevisionInProgressIssue revisionInProgress = FailedChecksRevision();

        // Act
        DateTimeOffset cutoff = Complete(revisionInProgress, transition);

        // Assert
        cutoff.ShouldBe(RunStartedAt);
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void WhenInputProvidedTrigger_CutoffIsRunStartedAtSoTheAnswerIsNotReadAgain(string transition)
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(RunStartedAt.AddHours(-2));
        RevisionInProgressIssue revisionInProgress = ((RevisionQueuedIssue)awaiting.ProvideInput("Use Postgres.", RunStartedAt.AddHours(-1), DateTimeOffset.UtcNow))
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);

        // Act
        DateTimeOffset cutoff = Complete(revisionInProgress, transition);

        // Assert
        cutoff.ShouldBe(RunStartedAt);
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void WhenRebaseConflictTrigger_CutoffIsRunStartedAt(string transition)
    {
        // Arrange
        RevisionInProgressIssue revisionInProgress = RebaseConflictRevision();

        // Act
        DateTimeOffset cutoff = Complete(revisionInProgress, transition);

        // Assert
        cutoff.ShouldBe(RunStartedAt);
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void WhenNonFeedbackTrigger_CommentPostedDuringRunIsAfterCutoff(string transition)
    {
        // Arrange
        RevisionInProgressIssue failedChecks = FailedChecksRevision();
        RevisionInProgressIssue rebaseConflict = RebaseConflictRevision();

        // Act
        DateTimeOffset failedChecksCutoff = Complete(failedChecks, transition);
        DateTimeOffset rebaseConflictCutoff = Complete(rebaseConflict, transition);

        // Assert
        failedChecksCutoff.ShouldSatisfyAllConditions(
            () => failedChecksCutoff.ShouldBeLessThan(CommentDuringRunAt),
            () => rebaseConflictCutoff.ShouldBeLessThan(CommentDuringRunAt));
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void WhenReviewFeedbackTrigger_CutoffIsNewestConsumedCommentAt(string transition)
    {
        // Arrange
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithNewestCommentAt(NewestConsumedCommentAt)
            .RevisionInProgress();

        // Act
        DateTimeOffset cutoff = Complete(revisionInProgress, transition);

        // Assert
        cutoff.ShouldBe(NewestConsumedCommentAt);
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void WhenReviewFeedbackTriggerWithoutConsumedTimestamp_CutoffIsRunStartedAt(string transition)
    {
        // Arrange
        RevisionInProgressIssue revisionInProgress = new IssueBuilder().RevisionInProgress();

        // Act
        DateTimeOffset cutoff = Complete(revisionInProgress, transition);

        // Assert
        cutoff.ShouldBe(RunStartedAt);
    }

    private static RevisionInProgressIssue FailedChecksRevision()
    {
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .ChecksPending()
            .FixChecks([new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1")], DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
        revisionInProgress.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
        return revisionInProgress;
    }

    private static RevisionInProgressIssue RebaseConflictRevision()
    {
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .RebasePending()
            .ResolveConflict(DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
        revisionInProgress.Trigger.ShouldBeOfType<RevisionTrigger.RebaseConflict>();
        return revisionInProgress;
    }

    private static DateTimeOffset Complete(RevisionInProgressIssue revisionInProgress, string transition) =>
        transition switch
        {
            nameof(RevisionInProgressIssue.MarkRebasePending) => revisionInProgress.MarkRebasePending(RunStartedAt,
            DateTimeOffset.UtcNow).FeedbackCutoffAt,
            nameof(RevisionInProgressIssue.MarkUnchangedRebasePending) =>
                revisionInProgress.MarkUnchangedRebasePending(RunStartedAt, DateTimeOffset.UtcNow).FeedbackCutoffAt,
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, null),
        };
}
