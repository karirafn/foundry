using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.IssueWatchTests;

/// <summary>The combined settle rule (ADR 0096) covering a moved branch head (issue #1035).</summary>
public sealed class SettleHead
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly IssueBodyHash BaselineBody = IssueBodyHash.Of("baseline");
    private static readonly BranchHead Baseline = new BranchHead.Present("aaa111");

    private static readonly ReviewFeedback NoComments = new([], OmittedCommentCount: 0, NewestCommentAt: null);

    private static IssueWatch WatchPushedAt(DateTimeOffset firstSeenAt, BranchHead? baseline = null)
    {
        IssueWatch watch = IssueWatch.Start(RepositoryId, 42, BaselineBody, baseline ?? Baseline);
        watch.ObserveHead(new BranchHead.Present("bbb222"), firstSeenAt);
        return watch;
    }

    [Fact]
    public void WhenAPushHasNotSettled_WaitsUntilTwoMinutesAfterItWasFirstSeen()
    {
        // Arrange
        IssueWatch sut = WatchPushedAt(Now.AddMinutes(-1));

        // Act
        InputSettlement result = sut.Settle(NoComments, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenAPushSettled_ArrivesWithABranchUpdatedPartFromTheBaselineAtItsFirstSeenTime()
    {
        // Arrange
        IssueWatch sut = WatchPushedAt(Now.AddMinutes(-2));

        // Act
        InputSettlement result = sut.Settle(NoComments, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Settled(
            new ClarificationInput([new InputPart.BranchUpdated("aaa111", "bbb222")]),
            Now.AddMinutes(-2)));
    }

    [Fact]
    public void WhenABurstOfPushesSettled_ArrivesOnceFromTheBaselineToTheLatestHead()
    {
        // Arrange
        IssueWatch sut = WatchPushedAt(Now.AddMinutes(-4));
        sut.ObserveHead(new BranchHead.Present("ccc333"), Now.AddMinutes(-3));
        sut.ObserveHead(new BranchHead.Present("ddd444"), Now.AddMinutes(-1));

        // Act
        InputSettlement waiting = sut.Settle(NoComments, Now);
        InputSettlement settled = sut.Settle(NoComments, Now.AddMinutes(1));

        // Assert
        waiting.ShouldBe(new InputSettlement.Waiting(Now.AddMinutes(1)));
        settled.ShouldBe(new InputSettlement.Settled(
            new ClarificationInput([new InputPart.BranchUpdated("aaa111", "ddd444")]),
            Now.AddMinutes(-1)));
    }

    [Fact]
    public void WhenAPushIsRevertedToTheBaselineWithinTheQuietPeriod_NothingArrives()
    {
        // Arrange
        IssueWatch sut = WatchPushedAt(Now.AddMinutes(-1));
        sut.ObserveHead(Baseline, Now);

        // Act
        InputSettlement result = sut.Settle(NoComments, Now.AddMinutes(2));

        // Assert
        result.ShouldBeOfType<InputSettlement.NothingNew>();
    }

    [Fact]
    public void WhenTheBranchWasDeleted_NothingArrives()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, BaselineBody, Baseline);
        sut.ObserveHead(new BranchHead.Absent(), Now.AddMinutes(-5));

        // Act
        InputSettlement result = sut.Settle(NoComments, Now);

        // Assert
        result.ShouldBeOfType<InputSettlement.NothingNew>();
    }

    [Fact]
    public void WhenTheBranchWasRecreated_ArrivesWithoutAFromSha()
    {
        // Arrange
        IssueWatch sut = WatchPushedAt(Now.AddMinutes(-2), new BranchHead.Absent());

        // Act
        InputSettlement result = sut.Settle(NoComments, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Settled(
            new ClarificationInput([new InputPart.BranchUpdated(null, "bbb222")]),
            Now.AddMinutes(-2)));
    }

    [Fact]
    public void WhenACommentSettledButAPushIsHeld_WaitsForThePush()
    {
        // Arrange
        IssueWatch sut = WatchPushedAt(Now.AddSeconds(-30));
        ReviewFeedback comments = new([new ReviewComment("pushed it")], OmittedCommentCount: 0, Now.AddMinutes(-3));

        // Act
        InputSettlement result = sut.Settle(comments, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddSeconds(90)));
    }

    [Fact]
    public void WhenAPushSettledButACommentIsHeld_WaitsForTheComment()
    {
        // Arrange
        IssueWatch sut = WatchPushedAt(Now.AddMinutes(-5));

        // Act
        InputSettlement result = sut.Settle(NoComments with { HeldBackUntil = Now.AddSeconds(40) }, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddSeconds(40)));
    }

    [Fact]
    public void WhenACommentAnEditAndAPushAllSettled_ArrivesWithEveryPartAtTheNewestTime()
    {
        // Arrange
        IssueWatch sut = WatchPushedAt(Now.AddMinutes(-2));
        sut.ObserveBody(IssueBodyHash.Of("edited"), Now.AddMinutes(-3));
        ReviewFeedback comments = new([new ReviewComment("pushed it")], OmittedCommentCount: 0, Now.AddMinutes(-4));

        // Act
        InputSettlement result = sut.Settle(comments, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Settled(
            new ClarificationInput(
            [
                new InputPart.Comments("pushed it"),
                new InputPart.DescriptionEdited(),
                new InputPart.BranchUpdated("aaa111", "bbb222"),
            ]),
            Now.AddMinutes(-2)));
    }
}
