using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.IssueWatchTests;

/// <summary>The branch-head half of the Issue Watch (issue #1035): the same baseline and pending rules as the body.</summary>
public sealed class ObserveHead
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly BranchHead Baseline = new BranchHead.Present("aaa111");
    private static readonly BranchHead Pushed = new BranchHead.Present("bbb222");
    private static readonly BranchHead PushedAgain = new BranchHead.Present("ccc333");

    private static IssueWatch WatchFrom(BranchHead? baseline) =>
        IssueWatch.Start(RepositoryId, 42, baselineBodyHash: null, baseline);

    [Fact]
    public void WhenThereIsNoBaseline_RecordsTheReadAsTheBaselineWithoutAChange()
    {
        // Arrange
        IssueWatch sut = WatchFrom(null);

        // Act
        sut.ObserveHead(Pushed, Now);

        // Assert
        sut.BaselineHead.ShouldBe(Pushed);
        sut.PendingHeadChange.ShouldBeNull();
    }

    [Fact]
    public void WhenTheHeadMatchesTheBaseline_RecordsNothing()
    {
        // Arrange
        IssueWatch sut = WatchFrom(Baseline);

        // Act
        sut.ObserveHead(Baseline, Now);

        // Assert
        sut.PendingHeadChange.ShouldBeNull();
    }

    [Fact]
    public void WhenTheHeadDiffersFromTheBaseline_RecordsItAsPendingFirstSeenNow()
    {
        // Arrange
        IssueWatch sut = WatchFrom(Baseline);

        // Act
        sut.ObserveHead(Pushed, Now);

        // Assert
        sut.PendingHeadChange.ShouldBe(new PendingHeadChange("bbb222", Now));
        sut.BaselineHead.ShouldBe(Baseline);
    }

    [Fact]
    public void WhenThePendingHeadIsReadAgain_KeepsItsFirstSeenTime()
    {
        // Arrange
        IssueWatch sut = WatchFrom(Baseline);
        sut.ObserveHead(Pushed, Now);

        // Act
        sut.ObserveHead(Pushed, Now.AddMinutes(1));

        // Assert
        sut.PendingHeadChange.ShouldBe(new PendingHeadChange("bbb222", Now));
    }

    [Fact]
    public void WhenAnotherHeadIsRead_ReplacesThePendingChangeAndRestartsItsTime()
    {
        // Arrange
        IssueWatch sut = WatchFrom(Baseline);
        sut.ObserveHead(Pushed, Now);

        // Act
        sut.ObserveHead(PushedAgain, Now.AddMinutes(1));

        // Assert
        sut.PendingHeadChange.ShouldBe(new PendingHeadChange("ccc333", Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenTheBaselineIsReadAgain_ClearsThePendingChange()
    {
        // Arrange
        IssueWatch sut = WatchFrom(Baseline);
        sut.ObserveHead(Pushed, Now);

        // Act
        sut.ObserveHead(Baseline, Now.AddMinutes(1));

        // Assert
        sut.PendingHeadChange.ShouldBeNull();
    }

    [Fact]
    public void WhenTheBranchIsDeleted_RecordsNoChange()
    {
        // Arrange
        IssueWatch sut = WatchFrom(Baseline);

        // Act
        sut.ObserveHead(new BranchHead.Absent(), Now);

        // Assert
        sut.PendingHeadChange.ShouldBeNull();
        sut.BaselineHead.ShouldBe(Baseline);
    }

    [Fact]
    public void WhenTheBranchIsDeletedAfterAPush_ClearsThePendingChange()
    {
        // Arrange
        IssueWatch sut = WatchFrom(Baseline);
        sut.ObserveHead(Pushed, Now);

        // Act
        sut.ObserveHead(new BranchHead.Absent(), Now.AddMinutes(1));

        // Assert
        sut.PendingHeadChange.ShouldBeNull();
    }

    [Fact]
    public void WhenAnAbsentBranchIsCreated_RecordsItAsPending()
    {
        // Arrange
        IssueWatch sut = WatchFrom(new BranchHead.Absent());

        // Act
        sut.ObserveHead(Pushed, Now);

        // Assert
        sut.PendingHeadChange.ShouldBe(new PendingHeadChange("bbb222", Now));
    }
}
