using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// Every state instance records when the issue entered it: the detected state at detection, every later one at the
/// transition that created it, never the time the state being left was entered (#892).
/// </summary>
public sealed class EnteredStateAt
{
    private static readonly DateTimeOffset DetectedAt = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private static IssueBuilder Builder() => new IssueBuilder().WithDetectedAt(DetectedAt).WithTransitionedAt(Earlier);

    [Fact]
    public void WhenDetected_EnteredStateAtIsTheDetectionTime()
    {
        // Act
        DetectedIssue detected = Builder().Detected();

        // Assert
        detected.EnteredStateAt.ShouldBe(DetectedAt);
    }

    [Fact]
    public void WhenDetectedWithTheDefaultTime_Throws()
    {
        // Act
        Action detect = () => DetectedIssue.Detect(
            MonitoredRepositoryId.New(),
            1,
            "Test Issue",
            IssueAuthor.Create("octocat").ValueOrThrow(),
            ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow(),
            ["foundry"],
            DateTimeOffset.MinValue,
            KindClassification.Unclassified);

        // Assert
        detect.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WhenTransitionedWithTheDefaultTime_Throws()
    {
        // Arrange
        FreshQueuedIssue queued = Builder().FreshQueued();

        // Act
        Action claim = () => queued.Claim(WorkerRunId.New(), DateTimeOffset.MinValue);

        // Assert
        claim.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WhenTriageQueued_EnteredStateAtIsTheTransitionTime()
    {
        // Arrange
        DetectedIssue detected = Builder().Detected();

        // Act
        TriageQueuedIssue queued = detected.QueueTriage(Later);

        // Assert
        queued.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenTriageClaimed_EnteredStateAtMovesForwardToTheClaimTime()
    {
        // Arrange
        TriageQueuedIssue queued = Builder().TriageQueued();

        // Act
        TriageInProgressIssue triaging = queued.Claim(WorkerRunId.New(), Later);

        // Assert
        triaging.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenTriageBlocked_EnteredStateAtIsTheTransitionTime()
    {
        // Arrange
        TriageQueuedIssue queued = Builder().TriageQueued();

        // Act
        TriageBlockedIssue blocked = queued.Block([2], Later);

        // Assert
        blocked.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenImplementClaimed_EnteredStateAtMovesForwardToTheClaimTime()
    {
        // Arrange
        FreshQueuedIssue queued = Builder().FreshQueued();

        // Act
        InProgressIssue inProgress = queued.Claim(WorkerRunId.New(), Later);

        // Assert
        inProgress.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenReleasedThroughTheDependencyCheckIntoBlocked_EnteredStateAtIsTheTransitionTime()
    {
        // Arrange
        TriageFailedIssue triageFailed = Builder().WithBlockedBy([2]).TriageFailed();

        // Act
        Issue blocked = triageFailed.SkipTriage(Later);

        // Assert
        blocked.ShouldBeOfType<BlockedIssue>().EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenUnblocked_EnteredStateAtMovesForwardToTheTransitionTime()
    {
        // Arrange
        BlockedIssue blocked = (BlockedIssue)Builder().WithBlockedBy([2]).TriageFailed().SkipTriage(Earlier);

        // Act
        FreshQueuedIssue queued = blocked.Unblock(Later);

        // Assert
        queued.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenMovedToReview_EnteredStateAtMovesForwardToTheTransitionTime()
    {
        // Arrange
        ChecksPendingIssue checksPending = Builder().ChecksPending();

        // Act
        ReviewIssue review = checksPending.ToReview(new ChecksOutcome.Passed(), "0a1b2c3d", Later);

        // Assert
        review.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenRevisionRequestedFromReview_EnteredStateAtMovesForwardToTheTransitionTime()
    {
        // Arrange
        ReviewIssue review = Builder().Review();

        // Act
        RevisionQueuedIssue revisionQueued = review.Revise([new ReviewComment("Please fix.")], Later);

        // Assert
        revisionQueued.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenFailed_EnteredStateAtIsTheFailureTime()
    {
        // Arrange
        InProgressIssue inProgress = Builder().InProgress();

        // Act
        FailedIssue failed = inProgress.MarkFailed("Container exited with code 1", Later, FailureCategory.NonZeroExit);

        // Assert
        failed.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenCompleted_EnteredStateAtIsTheCompletionTime()
    {
        // Arrange
        ReviewIssue review = Builder().Review();

        // Act
        CompletedIssue completed = review.Complete(Later);

        // Assert
        completed.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenRequeuedIntoTheSameStateType_TheNewInstanceStampsTheTransitionTime()
    {
        // Arrange
        RebasePendingIssue rebasePending = Builder().RebasePending();

        // Act
        RebasePendingIssue next = rebasePending
            .ResolveConflict(Earlier)
            .Claim(WorkerRunId.New(), Earlier)
            .MarkRebasePending(Earlier, Later);

        // Assert
        next.EnteredStateAt.ShouldBe(Later);
    }

    [Fact]
    public void WhenTransitioned_TheTimeTheLeftStateWasEnteredIsNotCarriedOver()
    {
        // Arrange
        InProgressIssue inProgress = Builder().InProgress();

        // Act
        UnchangedIssue unchanged = inProgress.MarkUnchanged(Later);

        // Assert
        unchanged.EnteredStateAt.ShouldNotBe(inProgress.EnteredStateAt);
    }
}
