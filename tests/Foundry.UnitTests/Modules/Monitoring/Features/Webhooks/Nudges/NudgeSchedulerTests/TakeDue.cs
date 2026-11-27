using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.Nudges.NudgeSchedulerTests;

public sealed class TakeDue
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();
    private static readonly NudgeKey Key = new(RepoId, NudgePass.Checks, 7);

    private static bool IsMonitored(MonitoredRepositoryId id) => id == RepoId;

    [Fact]
    public void WhenSeveralNudgesForOneKeyArePending_RunsOnce()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.Add(new Nudge(Key), T0.AddSeconds(1));
        scheduler.Add(new Nudge(Key), T0.AddSeconds(2));

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0.AddSeconds(2), IsMonitored);

        // Assert
        due.ShouldBe([Key]);
        scheduler.TakeDue(T0.AddSeconds(2), IsMonitored).ShouldBeEmpty();
    }

    [Fact]
    public void WhenSingleNudgeArrives_IsDueAfterTheCoalescingWindow()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0 + NudgeScheduler.CoalescingWindow, IsMonitored);

        // Assert
        due.ShouldBe([Key]);
    }

    [Fact]
    public void WhenKeyRanFiveSecondsAgoAndRepositoryHasFiveMinutePollInterval_TrailingRunIsStillAtRunPlusFloor()
    {
        // Arrange — the scheduler never sees the repository's poll interval, so the floor is fixed
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IsMonitored);
        scheduler.Add(new Nudge(Key), T0.AddSeconds(5));

        // Act
        IReadOnlyList<NudgeKey> early = scheduler.TakeDue(T0.AddSeconds(9), IsMonitored);
        IReadOnlyList<NudgeKey> trailing = scheduler.TakeDue(T0.AddSeconds(10), IsMonitored);

        // Assert
        early.ShouldBeEmpty();
        trailing.ShouldBe([Key]);
    }

    [Fact]
    public void WhenKeyRanLessThanOneFloorAgo_CoalescesIntoOneTrailingRunAtFloorEnd()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IsMonitored);

        scheduler.Add(new Nudge(Key), T0.AddSeconds(5));
        scheduler.Add(new Nudge(Key), T0.AddSeconds(10));

        // Act
        IReadOnlyList<NudgeKey> early = scheduler.TakeDue(T0 + NudgeScheduler.RateFloor - TimeSpan.FromSeconds(1), IsMonitored);
        DateTimeOffset? wake = scheduler.NextWakeAt;
        IReadOnlyList<NudgeKey> trailing = scheduler.TakeDue(T0 + NudgeScheduler.RateFloor, IsMonitored);

        // Assert
        early.ShouldBeEmpty();
        wake.ShouldBe(T0 + NudgeScheduler.RateFloor);
        trailing.ShouldBe([Key]);
    }

    [Fact]
    public void WhenNudgeArrivesExactlyOneFloorAfterPreviousRun_RunsImmediately()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IsMonitored);
        scheduler.Add(new Nudge(Key), T0 + NudgeScheduler.RateFloor);

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0 + NudgeScheduler.RateFloor, IsMonitored);

        // Assert
        due.ShouldBe([Key]);
    }

    [Fact]
    public void WhenNotBeforeInFuture_WaitsUntilDeadline()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        DateTimeOffset deadline = T0.AddMinutes(5);
        scheduler.Add(new Nudge(Key, deadline), T0);

        // Act
        IReadOnlyList<NudgeKey> before = scheduler.TakeDue(deadline.AddSeconds(-1), IsMonitored);
        DateTimeOffset? wake = scheduler.NextWakeAt;
        IReadOnlyList<NudgeKey> at = scheduler.TakeDue(deadline, IsMonitored);

        // Assert
        before.ShouldBeEmpty();
        wake.ShouldBe(deadline);
        at.ShouldBe([Key]);
    }

    [Fact]
    public void WhenRepositoryNoLongerMonitored_DropsNudge()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        NudgeKey orphan = new(MonitoredRepositoryId.New(), NudgePass.IssueListing);
        scheduler.Add(new Nudge(orphan), T0);

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0, IsMonitored);

        // Assert
        due.ShouldBeEmpty();
        scheduler.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void WhenKeysDiffer_EachRunsIndependently()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        NudgeKey otherIssue = Key with { IssueNumber = 8 };
        NudgeKey listing = new(RepoId, NudgePass.IssueListing);
        scheduler.Add(new Nudge(Key), T0);
        scheduler.Add(new Nudge(otherIssue), T0);
        scheduler.Add(new Nudge(listing), T0);

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0, IsMonitored);

        // Assert
        due.Count.ShouldBe(3);
    }

    [Fact]
    public void WhenIdenticalDeferredNudgesAreAdded_KeepsOne()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        Nudge deferred = new(Key, T0.AddMinutes(2));

        // Act
        scheduler.Add(deferred, T0);
        scheduler.Add(deferred, T0 + NudgeScheduler.RateFloor);

        // Assert
        scheduler.PendingCount.ShouldBe(1);
    }

    [Fact]
    public void WhenReviewPassDefersFollowUpForHeldComment_RunsItAtTheQuietPeriodEnd()
    {
        // Arrange — the nudged review pass ran at T0 and held a comment back until T0 + 2 min
        NudgeKey reviewKey = new(RepoId, NudgePass.Review, 7);
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(reviewKey), T0);
        scheduler.TakeDue(T0, IsMonitored);
        scheduler.Add(new Nudge(reviewKey, T0.AddMinutes(2)), T0.AddSeconds(1));

        // Act
        IReadOnlyList<NudgeKey> early = scheduler.TakeDue(T0.AddMinutes(1), IsMonitored);
        DateTimeOffset? wake = scheduler.NextWakeAt;
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0.AddMinutes(2), IsMonitored);

        // Assert
        early.ShouldBeEmpty();
        wake.ShouldBe(T0.AddMinutes(2));
        due.ShouldBe([reviewKey]);
    }

    [Fact]
    public void WhenDeadlineArrivesLessThanOneFloorAfterPreviousRun_RunsAtTheDeadline()
    {
        // Arrange — the key ran at T0; a deadline nudge for T0 + 5 s is scheduled right after
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IsMonitored);
        DateTimeOffset deadline = T0.AddSeconds(5);
        scheduler.Add(new Nudge(Key, deadline), T0.AddSeconds(1));

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(deadline, IsMonitored);

        // Assert
        due.ShouldBe([Key]);
    }

    [Fact]
    public void WhenDeadlineNudgeRuns_DoesNotStampTheFloorWindow()
    {
        // Arrange — a deadline run at T0 + 5 s, then a webhook nudge one second later
        NudgeScheduler scheduler = new();
        DateTimeOffset deadline = T0.AddSeconds(5);
        scheduler.Add(new Nudge(Key, deadline), T0);
        scheduler.TakeDue(deadline, IsMonitored);
        scheduler.Add(new Nudge(Key), deadline.AddSeconds(1));

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(deadline.AddSeconds(1), IsMonitored);

        // Assert — no webhook-originated run preceded it, so nothing floors it
        due.ShouldBe([Key]);
    }

    [Fact]
    public void WhenDeadlineMergesWithFlooredWebhookNudge_RunsOnceImmediatelyAndConsumesIt()
    {
        // Arrange — a webhook run at T0, a floored webhook nudge waiting for T0 + 10 s, a deadline at T0 + 8 s
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IsMonitored);
        scheduler.Add(new Nudge(Key), T0.AddSeconds(5));
        DateTimeOffset deadline = T0.AddSeconds(8);
        scheduler.Add(new Nudge(Key, deadline), T0.AddSeconds(6));

        // Act
        IReadOnlyList<NudgeKey> atDeadline = scheduler.TakeDue(deadline, IsMonitored);
        IReadOnlyList<NudgeKey> atFloorEnd = scheduler.TakeDue(T0 + NudgeScheduler.RateFloor, IsMonitored);
        scheduler.Add(new Nudge(Key), T0 + NudgeScheduler.RateFloor);
        IReadOnlyList<NudgeKey> nextWebhook = scheduler.TakeDue(T0 + NudgeScheduler.RateFloor, IsMonitored);

        // Assert — the deadline run consumed the floored nudge and left the floor window at T0
        atDeadline.ShouldBe([Key]);
        atFloorEnd.ShouldBeEmpty();
        nextWebhook.ShouldBe([Key]);
    }

    [Fact]
    public void WhenRebaseStepSchedulesLandingRecheck_RunsAfterLandingPollDelay()
    {
        // Arrange — the rebase pass ran at T0 and requested the rebase
        NudgeKey rebaseKey = new(RepoId, NudgePass.Rebase, 7);
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(rebaseKey), T0);
        scheduler.TakeDue(T0, _ => true);
        DateTimeOffset recheckAt = T0 + PendingRebase.LandingPollDelay;
        scheduler.Add(new Nudge(rebaseKey, recheckAt), T0);

        // Act
        DateTimeOffset? wake = scheduler.NextWakeAt;
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(recheckAt + NudgeScheduler.CoalescingWindow, _ => true);

        // Assert
        wake.ShouldBe(recheckAt);
        due.ShouldBe([rebaseKey]);
    }
}
