using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.IssueWatchTests;

/// <summary>The combined settle rule (ADR 0096): nothing arrives while any input is inside the quiet period.</summary>
public sealed class Settle
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly IssueBodyHash Baseline = IssueBodyHash.Of("baseline");
    private static readonly IssueBodyHash Edited = IssueBodyHash.Of("edited");

    private static readonly ReviewFeedback NoComments = new([], OmittedCommentCount: 0, NewestCommentAt: null);

    private static IssueWatch WatchEditedAt(DateTimeOffset firstSeenAt)
    {
        IssueWatch watch = IssueWatch.Start(RepositoryId, 42, Baseline);
        watch.ObserveBody(Edited, firstSeenAt);
        return watch;
    }

    private static ReviewFeedback Settled(DateTimeOffset newestAt, params string[] bodies) =>
        new([.. bodies.Select(body => new ReviewComment(body))], OmittedCommentCount: 0, newestAt);

    [Fact]
    public void WhenNothingArrived_ReturnsNothingNew()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline);

        // Act
        InputSettlement result = sut.Settle(NoComments, Now);

        // Assert
        result.ShouldBeOfType<InputSettlement.NothingNew>();
    }

    [Fact]
    public void WhenOnlyCommentsSettled_ArrivesWithACommentsPartAtTheNewestComment()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline);

        // Act
        InputSettlement result = sut.Settle(Settled(Now.AddMinutes(-3), " first ", "second"), Now);

        // Assert
        result.ShouldBe(new InputSettlement.Settled(
            ClarificationInput.FromComments("first\n\nsecond"),
            Now.AddMinutes(-3)));
    }

    [Fact]
    public void WhenAnEditHasNotSettled_WaitsUntilTwoMinutesAfterItWasFirstSeen()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddMinutes(-1));

        // Act
        InputSettlement result = sut.Settle(NoComments, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenAnEditSettled_ArrivesWithADescriptionEditedPartAtItsFirstSeenTime()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddMinutes(-2));

        // Act
        InputSettlement result = sut.Settle(NoComments, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Settled(
            new ClarificationInput([new InputPart.DescriptionEdited()]),
            Now.AddMinutes(-2)));
    }

    [Fact]
    public void WhenAnEditIsRevertedWithinTheQuietPeriod_NothingArrives()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddMinutes(-1));
        sut.ObserveBody(Baseline, Now);

        // Act
        InputSettlement result = sut.Settle(NoComments, Now.AddMinutes(2));

        // Assert
        result.ShouldBeOfType<InputSettlement.NothingNew>();
    }

    [Fact]
    public void WhenEditsKeepArriving_WaitsUntilTheLatestHasStayedForTheQuietPeriod()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddMinutes(-3));
        sut.ObserveBody(IssueBodyHash.Of("edited again"), Now.AddMinutes(-1));

        // Act
        InputSettlement result = sut.Settle(NoComments, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenOneCommentSettledAndAnotherIsHeld_NothingArrivesUntilTheHeldOneSettles()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline);
        ReviewFeedback feedback = Settled(Now.AddMinutes(-2), "first") with { HeldBackUntil = Now.AddMinutes(1) };

        // Act
        InputSettlement result = sut.Settle(feedback, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenACommentSettledButAnEditIsHeld_WaitsForTheEdit()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddSeconds(-30));

        // Act
        InputSettlement result = sut.Settle(Settled(Now.AddMinutes(-3), "answer"), Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddSeconds(90)));
    }

    [Fact]
    public void WhenAnEditSettledButACommentIsHeld_WaitsForTheComment()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddMinutes(-5));

        // Act
        InputSettlement result = sut.Settle(NoComments with { HeldBackUntil = Now.AddSeconds(40) }, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddSeconds(40)));
    }

    [Fact]
    public void WhenBothAreHeld_WaitsUntilTheLaterSettles()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddMinutes(-1));

        // Act
        InputSettlement result = sut.Settle(NoComments with { HeldBackUntil = Now.AddSeconds(30) }, Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenACommentAndAnEditBothSettled_ArrivesWithBothPartsAtTheNewerTime()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddMinutes(-2));

        // Act
        InputSettlement result = sut.Settle(Settled(Now.AddMinutes(-3), "answer"), Now);

        // Assert
        result.ShouldBe(new InputSettlement.Settled(
            new ClarificationInput([new InputPart.Comments("answer"), new InputPart.DescriptionEdited()]),
            Now.AddMinutes(-2)));
    }

    [Fact]
    public void WhenTheCommentIsNewerThanTheEdit_ArrivesAtTheCommentTime()
    {
        // Arrange
        IssueWatch sut = WatchEditedAt(Now.AddMinutes(-4));

        // Act
        InputSettlement result = sut.Settle(Settled(Now.AddMinutes(-2), "answer"), Now);

        // Assert
        result.ShouldBeOfType<InputSettlement.Settled>().ArrivedAt.ShouldBe(Now.AddMinutes(-2));
    }
}
