using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.ActiveRunTests;

public sealed class BeginPublishing
{
    [Fact]
    public void WhenTheContainerEnded_TheRunKeepsWhatItsOutcomeNeeds()
    {
        // Arrange
        ActiveRun active = PublishingRunFixture.ActiveRun();
        RunExit exit = new(1, OomKilled: true, RunStopReason.TimedOut, PublishingRunFixture.ExitedAt);

        // Act
        PublishingRun publishing = active.BeginPublishing(exit, "log tail", RunVerdict.Done(), new RunPullRequestText("s", "b"));

        // Assert
        publishing.ShouldSatisfyAllConditions(
            () => publishing.Id.ShouldBe(active.Id),
            () => publishing.Exit.ShouldBe(exit),
            () => publishing.BranchName.ShouldBe(PublishingRunFixture.Branch),
            () => publishing.Base.ShouldBe(PublishingRunFixture.Base),
            () => publishing.ContainerOutput.ShouldBe("log tail"),
            () => publishing.VerdictStatus.ShouldBe(RunVerdictStatus.Done),
            () => publishing.PullRequestText.ShouldBe(new RunPullRequestText("s", "b")),
            () => publishing.HasPublished.ShouldBeFalse());
    }

    [Fact]
    public void ARunWithoutABranch_CannotPublish()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(Foundry.Modules.Issues.Contracts.IssueId.New(), Foundry.Modules.Workers.Contracts.WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-1"),
            branchName: null,
            Foundry.Modules.Monitoring.Contracts.MonitoredRepositoryId.New());
        RunExit exit = new(0, OomKilled: false, RunStopReason.Exited, PublishingRunFixture.ExitedAt);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => active.BeginPublishing(exit, null, null, null));
    }
}
