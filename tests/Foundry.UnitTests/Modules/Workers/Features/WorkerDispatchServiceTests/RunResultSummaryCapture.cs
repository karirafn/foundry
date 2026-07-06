using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

/// <summary>
/// Verifies that RunResultSummary is captured from container output and persisted on
/// CompletedRun/FailedRun during terminal transitions.
/// </summary>
public sealed class RunResultSummaryCapture : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        IWorkerOrchestrator orchestrator,
        IContainerOutputParser containerOutputParser,
        IPostExitProviderQueries? postExitProviderQueries = null)
    {
        return base.BuildService(
            orchestrator,
            containerOutputParser: containerOutputParser,
            postExitProviderQueries: postExitProviderQueries);
    }

    [Fact]
    public async Task WhenSuccessWithCommitsAndPrFound_ResultSummarySetOnCompletedRun()
    {
        // Arrange — resolver uses GetMergeRequestByBranchAsync (MR-state-first), so stub the MR
        SeedActiveRun("container-summary-success");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "Done", subtype: null, isError: false,
            durationMs: 1000, numTurns: 3, totalCostUsd: 0.05m, inputTokens: 100, outputTokens: 50);
        ScriptedContainerOutputParser parser = new(runResultSummary: summary);
        StubPostExitProviderQueries queries = new(
            hasCommits: true,
            mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, "https://github.com/owner/repo/pull/1"));
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, logs: "some output");
        WorkerDispatchService sut = BuildService(orchestrator, parser, queries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        CompletedRun completedRun = run.ShouldBeOfType<CompletedRun>();
        completedRun.ResultSummary.ShouldNotBeNull();
        completedRun.ResultSummary!.NumTurns.ShouldBe(3);
    }

    [Fact]
    public async Task WhenNonZeroExit_ResultSummarySetOnFailedRun()
    {
        // Arrange
        SeedActiveRun("container-summary-failed");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "Error", subtype: "error", isError: true,
            durationMs: 500, numTurns: 1, totalCostUsd: 0.01m, inputTokens: 50, outputTokens: 10);
        ScriptedContainerOutputParser parser = new(runResultSummary: summary);
        StubPostExitProviderQueries queries = new(hasCommits: false);
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, logs: "error output");
        WorkerDispatchService sut = BuildService(orchestrator, parser, queries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ResultSummary.ShouldNotBeNull();
        failedRun.ResultSummary!.NumTurns.ShouldBe(1);
    }

    [Fact]
    public async Task WhenResultSummaryIsNull_TerminalTransitionStillSucceeds()
    {
        // Arrange
        SeedActiveRun("container-summary-null");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        ScriptedContainerOutputParser parser = new(runResultSummary: null);
        StubPostExitProviderQueries queries = new(hasCommits: false);
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, logs: "no result line");
        WorkerDispatchService sut = BuildService(orchestrator, parser, queries);

        // Act — must not throw when ParseRunResultSummary returns null
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — transition succeeded even with null summary
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ResultSummary.ShouldBeNull();
    }

    [Fact]
    public async Task WhenSuccessWithoutCommits_ResultSummarySetOnCompletedRun()
    {
        // Arrange
        SeedActiveRun("container-summary-no-commits");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "Done", subtype: null, isError: false,
            durationMs: 800, numTurns: 2, totalCostUsd: 0.02m, inputTokens: 80, outputTokens: 20);
        ScriptedContainerOutputParser parser = new(runResultSummary: summary);
        StubPostExitProviderQueries queries = new(hasCommits: false);
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, logs: "output");
        WorkerDispatchService sut = BuildService(orchestrator, parser, queries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        CompletedRun completedRun = run.ShouldBeOfType<CompletedRun>();
        completedRun.ResultSummary.ShouldNotBeNull();
        completedRun.ResultSummary!.NumTurns.ShouldBe(2);
    }

    [Fact]
    public async Task WhenSuccessWithCommitsButNoPr_ResultSummarySetOnFailedRun()
    {
        // Arrange
        SeedActiveRun("container-summary-no-pr");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "Done", subtype: null, isError: false,
            durationMs: 700, numTurns: 2, totalCostUsd: 0.02m, inputTokens: 60, outputTokens: 15);
        ScriptedContainerOutputParser parser = new(runResultSummary: summary);
        // has commits but no PR
        StubPostExitProviderQueries queries = new(hasCommits: true);
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, logs: "output");
        WorkerDispatchService sut = BuildService(orchestrator, parser, queries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ResultSummary.ShouldNotBeNull();
        failedRun.ResultSummary!.NumTurns.ShouldBe(2);
    }

    [Fact]
    public async Task WhenRunTimesOut_ResultSummarySetOnFailedRun()
    {
        // Arrange
        SeedActiveRun("container-summary-timeout");
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "Timed", subtype: null, isError: true,
            durationMs: 7200000, numTurns: 5, totalCostUsd: 0.5m, inputTokens: 500, outputTokens: 200);
        ScriptedContainerOutputParser parser = new(runResultSummary: summary);
        StubGlobalSettingsQueries settingsQueries = new(maxConcurrent: 3, timeoutMinutes: 0);
        ExitedWorkerOrchestrator orchestrator = new(
            new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null),
            logs: "timeout output");
        WorkerDispatchService sut = base.BuildService(
            orchestrator,
            containerOutputParser: parser,
            settingsQueries: settingsQueries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ResultSummary.ShouldNotBeNull();
        failedRun.ResultSummary!.NumTurns.ShouldBe(5);
    }

    /// <summary>
    /// Scripted parser that returns a configured RunResultSummary and a NormalExit parse result.
    /// </summary>
    private sealed class ScriptedContainerOutputParser(RunResultSummary? runResultSummary)
        : IContainerOutputParser
    {
        public ContainerOutputParseResult Parse(string? log, int defaultCooldownMinutes)
            => new ContainerOutputParseResult.NormalExit();

        public RunResultSummary? ParseRunResultSummary(string? log) => runResultSummary;
    }

    private sealed class ExitedWorkerOrchestrator(WorkerStatus status, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in summary tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(status is null
                ? new WorkerStatusProbe.NotFound()
                : new WorkerStatusProbe.Available(status));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult(logs);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }
}
