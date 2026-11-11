using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeResolverTests;

public sealed partial class ResolveAsync
{
    private static readonly RunVerdict NeedsInputVerdict =
        RunVerdict.NeedsInput([new RunVerdictQuestion("Which database?", "Two are configured.")]);

    [Fact]
    public async Task WhenExitZeroWithNeedsInputAndOpenMr_ReturnsNeedsInputOnThePullRequest()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
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
        WorkerOutcome.NeedsInput needsInput = outcome.ShouldBeOfType<WorkerOutcome.NeedsInput>();
        needsInput.ShouldSatisfyAllConditions(
            () => needsInput.PullRequestUrl.ShouldBe(PullRequestUrl.From(PrUrl)),
            () => needsInput.BranchHasCommits.ShouldBeTrue(),
            () => needsInput.Questions.ShouldBe(NeedsInputVerdict.Questions));
    }

    [Fact]
    public async Task WhenExitZeroWithNeedsInputAndMergedMr_ReturnsCompleted()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            mrResults: [Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Merged, PrUrl))]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            Exited(0),
            containerOutput: null,
            NeedsInputVerdict,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Completed>();
    }

    [Fact]
    public async Task WhenNonZeroExitWithNeedsInput_ReturnsFailure()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        IPostExitProviderQueries queries = new ScriptedProviderQueries(commitsResult: Result<bool>.Ok(false));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            Exited(1),
            containerOutput: null,
            NeedsInputVerdict,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }

    [Fact]
    public async Task WhenExitZeroWithNeedsInputAndCommitsButNoMr_ReturnsNeedsInputWithCommits()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
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
        WorkerOutcome.NeedsInput needsInput = outcome.ShouldBeOfType<WorkerOutcome.NeedsInput>();
        needsInput.ShouldSatisfyAllConditions(
            () => needsInput.PullRequestUrl.ShouldBeNull(),
            () => needsInput.BranchHasCommits.ShouldBeTrue(),
            () => needsInput.BranchName.Value.ShouldBe(DefaultBranchName));
    }

    [Fact]
    public async Task WhenExitZeroWithNeedsInputAndNothingPushed_ReturnsNeedsInputWithoutCommits()
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
        WorkerOutcome.NeedsInput needsInput = outcome.ShouldBeOfType<WorkerOutcome.NeedsInput>();
        needsInput.ShouldSatisfyAllConditions(
            () => needsInput.PullRequestUrl.ShouldBeNull(),
            () => needsInput.BranchHasCommits.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenExitZeroWithDoneVerdictAndOpenMr_ReturnsReview()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            mrResults: [Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl))]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            Exited(0),
            containerOutput: null,
            RunVerdict.Done(),
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Review>();
    }
}
