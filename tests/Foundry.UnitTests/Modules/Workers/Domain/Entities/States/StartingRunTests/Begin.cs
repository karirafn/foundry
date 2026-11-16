using System.Buffers.Text;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.StartingRunTests;

public sealed class Begin
{
    [Fact]
    public void MintsASourceTokenOfAtLeast32RandomBytesInBase64Url()
    {
        // Act
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Assert
        RunSourceToken token = run.SourceToken.ShouldNotBeNull();
        Base64Url.IsValid(token.Value, out int decodedLength).ShouldBeTrue();
        decodedLength.ShouldBeGreaterThanOrEqualTo(32);
    }

    [Fact]
    public void StoresOnlyTheTokensSha256Hash()
    {
        // Act
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Assert
        run.SourceTokenHash.ShouldBe(RunSourceTokenHash.Of(run.SourceToken!.Value));
    }

    [Fact]
    public void EachRunMintsItsOwnToken()
    {
        // Act
        StartingRun first = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        StartingRun second = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Assert
        first.SourceToken!.Value.ShouldNotBe(second.SourceToken!.Value);
    }

    [Fact]
    public void TheTokenNeverShowsInItsTextForm()
    {
        // Act
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Assert
        run.SourceToken!.ToString().ShouldNotContain(run.SourceToken.Value);
    }

    [Fact]
    public void TheHashIsKeptThroughActiveAndPublishing()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        RunSourceTokenHash? minted = starting.SourceTokenHash;

        // Act
        ActiveRun active = starting.Activate(ContainerId.From("container-1"), PublishingRunFixture.Branch, MonitoredRepositoryId.New());
        PublishingRun publishing = active.BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, PublishingRunFixture.ExitedAt),
            containerOutput: "log tail",
            RunVerdict.Done(),
            new RunPullRequestText("Add retry", "Body"));

        // Assert
        active.SourceTokenHash.ShouldBe(minted);
        publishing.SourceTokenHash.ShouldBe(minted);
    }

    [Fact]
    public void TheHashIsKeptOnAFailedRun()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Act
        FailedRun failed = starting.Fail(new FailureReason.ContainerError("image not found"));

        // Assert
        failed.SourceTokenHash.ShouldBe(starting.SourceTokenHash);
    }
}
