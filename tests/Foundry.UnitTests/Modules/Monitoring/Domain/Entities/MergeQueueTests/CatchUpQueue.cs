using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.MergeQueueTests;

/// <summary>The catch-up queue rules (ADR 0111): membership, head selection, the up-to-date wait and the round cap.</summary>
public sealed class CatchUpQueue
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset Episode = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 11, 0, 0, TimeSpan.Zero);

    private static MergePendingIssueInfo Issue(int number, DateTimeOffset? enteredAt = null) =>
        new(number, $"https://github.com/octocat/hello-world/pull/{number}", $"feat/{number}-thing", "abc123", enteredAt ?? Episode, enteredAt ?? Episode);

    private static MergeQueueIssueInfo QueueIssue(int number, bool isCatchingUp = false, bool hasPersonFeedback = false) =>
        new(number, $"feat/{number}-thing", isCatchingUp, hasPersonFeedback);

    private static PendingMerge Waiting(int number, DateTimeOffset queuedAt)
    {
        PendingMerge row = PendingMerge.Start(RepositoryId, number, Episode);
        row.AwaitCatchUp(queuedAt);
        return row;
    }

    private static PendingMerge CatchingUp(int number, DateTimeOffset queuedAt)
    {
        PendingMerge row = Waiting(number, queuedAt);
        row.StartCatchUp(queuedAt);
        return row;
    }

    [Fact]
    public void RefuseBehind_BelowTheCap_QueuesTheRowToWait()
    {
        // Arrange
        PendingMerge row = PendingMerge.Start(RepositoryId, 1, Episode);

        // Act
        BehindRefusal refusal = MergeQueue.RefuseBehind(row, Now);

        // Assert
        refusal.ShouldBe(BehindRefusal.Queued);
        row.ShouldSatisfyAllConditions(
            () => row.QueuedAt.ShouldBe(Now),
            () => row.AwaitsCatchUp.ShouldBeTrue(),
            () => row.CatchUpRounds.ShouldBe(0));
    }

    [Fact]
    public void RefuseBehind_WhenAlreadyQueued_KeepsItsPlace()
    {
        // Arrange
        PendingMerge row = CatchingUp(1, Episode);
        row.Restart(Now);

        // Act
        MergeQueue.RefuseBehind(row, Now);

        // Assert
        row.QueuedAt.ShouldBe(Episode);
        row.CatchUpRounds.ShouldBe(1);
    }

    [Fact]
    public void RefuseBehind_AtTheCap_FellBehind()
    {
        // Arrange
        PendingMerge row = PendingMerge.Start(RepositoryId, 1, Episode);
        for (int round = 0; round < MergeQueue.MaxCatchUpRounds; round++)
        {
            row.AwaitCatchUp(Episode);
            row.StartCatchUp(Episode);
            row.Restart(Episode.AddHours(round + 1));
        }

        // Act
        BehindRefusal refusal = MergeQueue.RefuseBehind(row, Now);

        // Assert
        refusal.ShouldBe(BehindRefusal.FellBehind);
        row.AwaitsCatchUp.ShouldBeFalse();
    }

    [Fact]
    public void Head_IsTheEarliestQueuedRowThenTheLowestIssueNumber()
    {
        // Arrange
        PendingMerge later = Waiting(1, Now);
        PendingMerge tiedHigher = Waiting(5, Episode);
        PendingMerge tiedLower = Waiting(3, Episode);
        PendingMerge notQueued = PendingMerge.Start(RepositoryId, 2, Episode);
        MergeQueue queue = MergeQueue.From(RepositoryId, [later, tiedHigher, tiedLower, notQueued]);

        // Act
        PendingMerge? head = queue.Head(MergeQueue.HeldIssueNumbers([Issue(1), Issue(2), Issue(3), Issue(5)], []));

        // Assert
        head.ShouldBeSameAs(tiedLower);
    }

    [Fact]
    public void Head_SkipsRowsWhoseIssueIsNoLongerHeld()
    {
        // Arrange
        PendingMerge stale = Waiting(1, Episode);
        PendingMerge live = Waiting(2, Now);
        MergeQueue queue = MergeQueue.From(RepositoryId, [stale, live]);

        // Act
        PendingMerge? head = queue.Head(MergeQueue.HeldIssueNumbers([Issue(2)], []));

        // Assert
        head.ShouldBeSameAs(live);
    }

    [Fact]
    public void Head_KeepsACatchingUpEntryAtTheFront()
    {
        // Arrange
        PendingMerge catchingUp = CatchingUp(1, Episode);
        PendingMerge waiting = Waiting(2, Now);
        MergeQueue queue = MergeQueue.From(RepositoryId, [catchingUp, waiting]);

        // Act
        PendingMerge? head = queue.Head(MergeQueue.HeldIssueNumbers([Issue(2)], [QueueIssue(1, isCatchingUp: true)]));

        // Assert
        head.ShouldBeSameAs(catchingUp);
    }

    [Fact]
    public void Forget_KeepsTheRowOfAnIssueThatIsCatchingUp()
    {
        // Arrange
        PendingMerge catchingUp = CatchingUp(1, Episode);
        PendingMerge leftForReview = PendingMerge.Start(RepositoryId, 2, Episode);
        MergeQueue queue = MergeQueue.From(RepositoryId, [catchingUp, leftForReview]);

        // Act
        IReadOnlyList<PendingMerge> forgotten = queue.Forget(
            MergeQueue.HeldIssueNumbers([], [QueueIssue(1, isCatchingUp: true), QueueIssue(2)]));

        // Assert
        forgotten.ShouldHaveSingleItem().ShouldBeSameAs(leftForReview);
    }

    [Fact]
    public void Track_AnEntryBackFromACatchUp_KeepsItsPlaceAndRounds()
    {
        // Arrange
        PendingMerge row = CatchingUp(1, Episode);
        row.RecordTransientFailure(Episode);
        MergeQueue queue = MergeQueue.From(RepositoryId, [row]);

        // Act
        PendingMerge tracked = queue.Track(Issue(1, Now)).Merge;

        // Assert
        tracked.ShouldSatisfyAllConditions(
            () => tracked.QueuedAt.ShouldBe(Episode),
            () => tracked.CatchUpRounds.ShouldBe(1),
            () => tracked.AwaitsCatchUp.ShouldBeFalse(),
            () => tracked.IsResolved.ShouldBeFalse(),
            () => tracked.TransientFailures.ShouldBe(0),
            () => tracked.NextAttemptAt.ShouldBeNull());
    }

    [Fact]
    public void Track_AnEntryThatLeftWhileWaiting_RejoinsUnqueued()
    {
        // Arrange
        PendingMerge row = Waiting(1, Episode);
        MergeQueue queue = MergeQueue.From(RepositoryId, [row]);

        // Act
        PendingMerge tracked = queue.Track(Issue(1, Now)).Merge;

        // Assert
        tracked.IsQueued.ShouldBeFalse();
        tracked.AwaitsCatchUp.ShouldBeFalse();
    }

    [Fact]
    public void AwaitableIssues_AreUnqueuedIssuesWithoutPersonFeedbackOtherThanTheHead()
    {
        // Arrange
        PendingMerge head = Waiting(1, Episode);
        PendingMerge queued = Waiting(2, Now);
        PendingMerge resolved = PendingMerge.Start(RepositoryId, 3, Episode);
        resolved.MarkResolved(Now);
        PendingMerge merging = PendingMerge.Start(RepositoryId, 4, Episode);
        MergeQueue queue = MergeQueue.From(RepositoryId, [head, queued, resolved, merging]);

        // Act
        IReadOnlyList<MergeQueueIssueInfo> awaitable = queue.AwaitableIssues(
            head,
            [
                QueueIssue(1),
                QueueIssue(2),
                QueueIssue(3),
                QueueIssue(4),
                QueueIssue(5),
                QueueIssue(6, hasPersonFeedback: true),
                QueueIssue(7, isCatchingUp: true),
            ]);

        // Assert
        awaitable.Select(i => i.IssueNumber).ShouldBe([4, 5]);
    }

    [Fact]
    public void StartCatchUp_WhenNotWaiting_Throws()
    {
        // Arrange
        PendingMerge row = PendingMerge.Start(RepositoryId, 1, Episode);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => row.StartCatchUp(Now));
    }

    [Fact]
    public void MarkResolved_LeavesTheQueue()
    {
        // Arrange
        PendingMerge row = CatchingUp(1, Episode);
        row.Restart(Now);

        // Act
        row.MarkResolved(Now);

        // Assert
        row.IsQueued.ShouldBeFalse();
        row.CatchUpRounds.ShouldBe(0);
    }
}
