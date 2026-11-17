using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.IssueWatchTests;

public sealed class ObserveBody
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly IssueBodyHash Baseline = IssueBodyHash.Of("baseline");
    private static readonly IssueBodyHash Edited = IssueBodyHash.Of("edited");
    private static readonly IssueBodyHash EditedAgain = IssueBodyHash.Of("edited again");

    [Fact]
    public void WhenThereIsNoBaseline_RecordsTheReadAsTheBaselineWithoutAChange()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, baselineBodyHash: null);

        // Act
        sut.ObserveBody(Edited, Now);

        // Assert
        sut.BaselineBodyHash.ShouldBe(Edited);
        sut.PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public void WhenTheBodyMatchesTheBaseline_RecordsNothing()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline);

        // Act
        sut.ObserveBody(Baseline, Now);

        // Assert
        sut.PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public void WhenTheBodyDiffersFromTheBaseline_RecordsItAsPendingFirstSeenNow()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline);

        // Act
        sut.ObserveBody(Edited, Now);

        // Assert
        sut.PendingBodyChange.ShouldBe(new PendingBodyChange(Edited, Now));
        sut.BaselineBodyHash.ShouldBe(Baseline);
    }

    [Fact]
    public void WhenThePendingBodyIsReadAgain_KeepsItsFirstSeenTime()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline);
        sut.ObserveBody(Edited, Now);

        // Act
        sut.ObserveBody(Edited, Now.AddMinutes(1));

        // Assert
        sut.PendingBodyChange.ShouldBe(new PendingBodyChange(Edited, Now));
    }

    [Fact]
    public void WhenAnotherBodyIsRead_ReplacesThePendingChangeAndRestartsItsTime()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline);
        sut.ObserveBody(Edited, Now);

        // Act
        sut.ObserveBody(EditedAgain, Now.AddMinutes(1));

        // Assert
        sut.PendingBodyChange.ShouldBe(new PendingBodyChange(EditedAgain, Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenTheBaselineIsReadAgain_ClearsThePendingChange()
    {
        // Arrange
        IssueWatch sut = IssueWatch.Start(RepositoryId, 42, Baseline);
        sut.ObserveBody(Edited, Now);

        // Act
        sut.ObserveBody(Baseline, Now.AddMinutes(1));

        // Assert
        sut.PendingBodyChange.ShouldBeNull();
    }
}
