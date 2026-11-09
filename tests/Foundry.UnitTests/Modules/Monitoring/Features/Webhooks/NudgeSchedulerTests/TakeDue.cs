using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.NudgeSchedulerTests;

public sealed class TakeDue
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();
    private static readonly NudgeKey Key = new(RepoId, NudgePass.Checks, 7);

    private static TimeSpan? IntervalFor(MonitoredRepositoryId id) => id == RepoId ? Interval : null;

    [Fact]
    public void WhenSeveralNudgesForOneKeyArePending_RunsOnce()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.Add(new Nudge(Key), T0.AddSeconds(1));
        scheduler.Add(new Nudge(Key), T0.AddSeconds(2));

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0.AddSeconds(2), IntervalFor);

        // Assert
        due.ShouldBe([Key]);
        scheduler.TakeDue(T0.AddSeconds(2), IntervalFor).ShouldBeEmpty();
    }

    [Fact]
    public void WhenKeyRanLessThanOneIntervalAgo_CoalescesIntoOneTrailingRunAtIntervalEnd()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IntervalFor);

        scheduler.Add(new Nudge(Key), T0.AddSeconds(5));
        scheduler.Add(new Nudge(Key), T0.AddSeconds(10));

        // Act
        IReadOnlyList<NudgeKey> early = scheduler.TakeDue(T0.AddSeconds(29), IntervalFor);
        DateTimeOffset? wake = scheduler.NextWakeAt;
        IReadOnlyList<NudgeKey> trailing = scheduler.TakeDue(T0.AddSeconds(30), IntervalFor);

        // Assert
        early.ShouldBeEmpty();
        wake.ShouldBe(T0.AddSeconds(30));
        trailing.ShouldBe([Key]);
    }

    [Fact]
    public void WhenNudgeArrivesExactlyOneIntervalAfterPreviousRun_RunsImmediately()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IntervalFor);
        scheduler.Add(new Nudge(Key), T0 + Interval);

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0 + Interval, IntervalFor);

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
        IReadOnlyList<NudgeKey> before = scheduler.TakeDue(deadline.AddSeconds(-1), IntervalFor);
        DateTimeOffset? wake = scheduler.NextWakeAt;
        IReadOnlyList<NudgeKey> at = scheduler.TakeDue(deadline, IntervalFor);

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
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0, IntervalFor);

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
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0, IntervalFor);

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
        scheduler.Add(deferred, T0.AddSeconds(30));

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
        scheduler.TakeDue(T0, IntervalFor);
        scheduler.Add(new Nudge(reviewKey, T0.AddMinutes(2)), T0.AddSeconds(1));

        // Act
        IReadOnlyList<NudgeKey> early = scheduler.TakeDue(T0.AddMinutes(1), IntervalFor);
        DateTimeOffset? wake = scheduler.NextWakeAt;
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(T0.AddMinutes(2), IntervalFor);

        // Assert
        early.ShouldBeEmpty();
        wake.ShouldBe(T0.AddMinutes(2));
        due.ShouldBe([reviewKey]);
    }

    [Fact]
    public void WhenDeadlineArrivesLessThanOneIntervalAfterPreviousRun_RunsAtTheDeadline()
    {
        // Arrange — the key ran at T0; a deadline nudge for T0 + 10 s is scheduled right after
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IntervalFor);
        DateTimeOffset deadline = T0.AddSeconds(10);
        scheduler.Add(new Nudge(Key, deadline), T0.AddSeconds(1));

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(deadline, IntervalFor);

        // Assert
        due.ShouldBe([Key]);
    }

    [Fact]
    public void WhenDeadlineNudgeRuns_DoesNotStampTheRateCapWindow()
    {
        // Arrange — a deadline run at T0 + 10 s, then a webhook nudge one second later
        NudgeScheduler scheduler = new();
        DateTimeOffset deadline = T0.AddSeconds(10);
        scheduler.Add(new Nudge(Key, deadline), T0);
        scheduler.TakeDue(deadline, IntervalFor);
        scheduler.Add(new Nudge(Key), deadline.AddSeconds(1));

        // Act
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(deadline.AddSeconds(1), IntervalFor);

        // Assert — no webhook-originated run preceded it, so nothing caps it
        due.ShouldBe([Key]);
    }

    [Fact]
    public void WhenDeadlineMergesWithCappedWebhookNudge_RunsOnceImmediatelyAndConsumesIt()
    {
        // Arrange — a webhook run at T0, a capped webhook nudge waiting for T0 + 30 s, a deadline at T0 + 10 s
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(Key), T0);
        scheduler.TakeDue(T0, IntervalFor);
        scheduler.Add(new Nudge(Key), T0.AddSeconds(5));
        DateTimeOffset deadline = T0.AddSeconds(10);
        scheduler.Add(new Nudge(Key, deadline), T0.AddSeconds(6));

        // Act
        IReadOnlyList<NudgeKey> atDeadline = scheduler.TakeDue(deadline, IntervalFor);
        IReadOnlyList<NudgeKey> atIntervalEnd = scheduler.TakeDue(T0 + Interval, IntervalFor);
        scheduler.Add(new Nudge(Key), T0 + Interval);
        IReadOnlyList<NudgeKey> nextWebhook = scheduler.TakeDue(T0 + Interval, IntervalFor);

        // Assert — the deadline run consumed the capped nudge and left the cap window at T0
        atDeadline.ShouldBe([Key]);
        atIntervalEnd.ShouldBeEmpty();
        nextWebhook.ShouldBe([Key]);
    }

    [Fact]
    public void WhenRebaseStepSchedulesLandingRecheck_RunsAfterLandingPollDelayNotOnePollIntervalLater()
    {
        // Arrange — a 5-minute poll interval; the rebase pass ran at T0 and requested the rebase
        TimeSpan pollInterval = TimeSpan.FromMinutes(5);
        NudgeKey rebaseKey = new(RepoId, NudgePass.Rebase, 7);
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(rebaseKey), T0);
        scheduler.TakeDue(T0, _ => pollInterval);
        DateTimeOffset recheckAt = T0 + PendingRebase.LandingPollDelay;
        scheduler.Add(new Nudge(rebaseKey, recheckAt), T0);

        // Act
        DateTimeOffset? wake = scheduler.NextWakeAt;
        IReadOnlyList<NudgeKey> due = scheduler.TakeDue(recheckAt + NudgeScheduler.CoalescingWindow, _ => pollInterval);

        // Assert
        wake.ShouldBe(recheckAt);
        due.ShouldBe([rebaseKey]);
    }
}
