using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.StartingRunTests;

/// <summary>A run started by Continue without answer refuses a <c>needs_input</c> verdict (ADR 0096).</summary>
public sealed class RefusesNeedsInput
{
    private static readonly RunVerdict NeedsInput =
        RunVerdict.NeedsInput([new RunVerdictQuestion("Which database?", "Two are configured.", "Postgres.")]);

    private static PublishingRun Publishing(bool refusesNeedsInput, RunVerdict verdict) =>
        StartingRun.Begin(IssueId.New(), WorkerRunId.New(), refusesNeedsInput: refusesNeedsInput)
            .Activate(ContainerId.From("container-1"), BranchName.From("feat/1-test"), MonitoredRepositoryId.New())
            .BeginPublishing(
                new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow),
                containerOutput: null,
                verdict,
                pullRequestText: null);

    [Fact]
    public void WhenBegunByContinueWithoutAnswer_EveryLaterStateCarriesTheFlag()
    {
        // Act
        PublishingRun publishing = Publishing(refusesNeedsInput: true, RunVerdict.Done());
        CompletedRun completed = publishing.Complete(0, publishing.BranchName, pullRequestUrl: null);

        // Assert
        publishing.RefusesNeedsInput.ShouldBeTrue();
        completed.RefusesNeedsInput.ShouldBeTrue();
    }

    [Fact]
    public void WhenBegunOtherwise_TheRunMayAsk()
    {
        // Act
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Assert
        starting.RefusesNeedsInput.ShouldBeFalse();
    }

    [Fact]
    public void WhenARunRefusingNeedsInputAsks_TheRefusalIsRecordedOnItsTerminalRunAndItDoesNotAskForInput()
    {
        // Arrange
        PublishingRun publishing = Publishing(refusesNeedsInput: true, NeedsInput);

        // Act
        CompletedRun completed = publishing.Complete(0, publishing.BranchName, pullRequestUrl: null);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.NeedsInputRefused.ShouldBeTrue(),
            () => completed.Verdict.ShouldBe(NeedsInput),
            () => publishing.AsksForInput.ShouldBeFalse());
    }

    [Fact]
    public void WhenARunNotStartedByContinueAsks_NothingIsRefused()
    {
        // Act
        PublishingRun publishing = Publishing(refusesNeedsInput: false, NeedsInput);

        // Assert
        publishing.ShouldSatisfyAllConditions(
            () => publishing.NeedsInputRefused.ShouldBeFalse(),
            () => publishing.AsksForInput.ShouldBeTrue());
    }

    [Fact]
    public void WhenARunRefusingNeedsInputReturnsDone_NothingIsRefused()
    {
        // Act
        PublishingRun publishing = Publishing(refusesNeedsInput: true, RunVerdict.Done());

        // Assert
        publishing.NeedsInputRefused.ShouldBeFalse();
    }
}
