using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.IssueWatchTests;

/// <summary>
/// The body-only settle a failed triage's watch uses (issue #773): the same quiet-period rule as an awaiting-input
/// round, with no comments.
/// </summary>
public sealed class SettleBodyChange
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly IssueBodyHash Baseline = IssueBodyHash.Of("baseline");
    private static readonly IssueBodyHash Edited = IssueBodyHash.Of("edited");

    [Fact]
    public void WhenStartedForAFailedTriage_WatchesTheFailedTriage()
    {
        // Act
        IssueWatch sut = IssueWatch.StartForFailedTriage(RepositoryId, 42, Baseline);

        // Assert
        sut.ShouldSatisfyAllConditions(
            () => sut.Wait.ShouldBe(WatchedWait.TriageFailed),
            () => sut.BaselineBodyHash.ShouldBe(Baseline),
            () => sut.PendingBodyChange.ShouldBeNull());
    }

    [Fact]
    public void WhenStartedForAQuestion_WatchesTheAwaitingInputWait()
    {
        // Act
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline, baselineHead: null);

        // Assert
        sut.Wait.ShouldBe(WatchedWait.AwaitingInput);
    }

    [Fact]
    public void WhenTheBodyIsUnchanged_ReturnsNothingNew()
    {
        // Arrange
        IssueWatch sut = IssueWatch.StartForFailedTriage(RepositoryId, 42, Baseline);
        sut.ObserveBody(Baseline, Now);

        // Act
        InputSettlement result = sut.SettleBodyChange(Now);

        // Assert
        result.ShouldBeOfType<InputSettlement.NothingNew>();
    }

    [Fact]
    public void WhenTheChangeIsInsideTheQuietPeriod_WaitsUntilItSettles()
    {
        // Arrange
        IssueWatch sut = IssueWatch.StartForFailedTriage(RepositoryId, 42, Baseline);
        sut.ObserveBody(Edited, Now.AddMinutes(-1));

        // Act
        InputSettlement result = sut.SettleBodyChange(Now);

        // Assert
        result.ShouldBe(new InputSettlement.Waiting(Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenTheChangeOutlastedTheQuietPeriod_SettlesAtItsFirstSeenTime()
    {
        // Arrange
        IssueWatch sut = IssueWatch.StartForFailedTriage(RepositoryId, 42, Baseline);
        sut.ObserveBody(Edited, Now.AddMinutes(-2));

        // Act
        InputSettlement result = sut.SettleBodyChange(Now);

        // Assert
        result.ShouldBe(new InputSettlement.Settled(
            new ClarificationInput([new InputPart.DescriptionEdited()]),
            Now.AddMinutes(-2)));
    }
}
