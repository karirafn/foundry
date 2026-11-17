using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeResolverTests;

/// <summary>
/// A run started by Continue without answer (ADR 0096) may not send its issue straight back to awaiting input: its
/// <c>needs_input</c> verdict is refused and the fact-based outcome applies.
/// </summary>
public sealed partial class ResolveAsync
{
    private static ActiveRun CreateRunRefusingNeedsInput()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), refusesNeedsInput: true);
        return starting.Activate(
            ContainerId.From("container-123"),
            BranchName.From(DefaultBranchName),
            MonitoredRepositoryId.New());
    }

    [Fact]
    public async Task WhenARunRefusingNeedsInputAsksWithAnOpenMr_ReturnsReview()
    {
        // Arrange
        ActiveRun run = CreateRunRefusingNeedsInput();
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            mrResults: [Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl))]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            Exited(0),
            containerOutput: null,
            NeedsInputVerdict,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Review>().PullRequestUrl.ShouldBe(PullRequestUrl.From(PrUrl));
    }

    [Fact]
    public async Task WhenARunRefusingNeedsInputAsksHavingPushedNothing_ReturnsUnchanged()
    {
        // Arrange
        ActiveRun run = CreateRunRefusingNeedsInput();
        IPostExitProviderQueries queries = new ScriptedProviderQueries(commitsResult: Result<bool>.Ok(false));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            Exited(0),
            containerOutput: null,
            NeedsInputVerdict,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Unchanged>();
    }

    [Fact]
    public async Task WhenARunRefusingNeedsInputAsksWithCommitsButNoMr_ReturnsAContinuableFailure()
    {
        // Arrange
        ActiveRun run = CreateRunRefusingNeedsInput();
        IPostExitProviderQueries queries = new ScriptedProviderQueries(commitsResult: Result<bool>.Ok(true));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            Exited(0),
            containerOutput: null,
            NeedsInputVerdict,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
    }

    [Fact]
    public async Task WhenARunNotStartedByContinueAsks_TheVerdictIsAccepted()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        IPostExitProviderQueries queries = new ScriptedProviderQueries(commitsResult: Result<bool>.Ok(false));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            Exited(0),
            containerOutput: null,
            NeedsInputVerdict,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.NeedsInput>();
    }
}
