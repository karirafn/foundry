using System.Runtime.CompilerServices;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Dispatch;

/// <summary>
/// Pre-ship integration test: when GetBranchCommitSummaryAsync returns NotFound,
/// ObserveRunningWorkerAsync resets BranchCommitCount to 0 (branch deleted scenario),
/// verified through the real module DI wiring.
/// </summary>
public sealed class WhenProviderReturnsNotFound : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenProviderReturnsNotFound()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IWorkerOrchestrator>();
            services.AddSingleton<IWorkerOrchestrator>(new RunningContainerOrchestrator());

            services.RemoveAll<IPostExitProviderQueries>();
            services.AddSingleton<IPostExitProviderQueries>(
                new SuccessThenNotFoundProviderQueries(
                    new BranchCommitSummary(CommitCount: 5, LatestSha: "sha-prior")));
        });
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task SeedActiveRunAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun activeRun = starting.Activate(
            ContainerId.From("container-notfound"),
            BranchName.From("feat/99-notfound"),
            MonitoredRepositoryId.New());
        dbContext.Set<WorkerRun>().Add(activeRun);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenProviderReturnsNotFound_BranchCommitCountResetToZero()
    {
        // Arrange
        await SeedActiveRunAsync();

        IServiceScopeFactory scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        using WorkerDispatchService sut = new(scopeFactory, NullLogger<WorkerDispatchService>.Instance);

        // Act — Tick 1: reconciliation; provider returns success with count = 5
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: provider returns NotFound — branch was deleted
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 3: another observation tick; provider still returns NotFound
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — BranchCommitCount is 0 (branch deleted → count reset)
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext db = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        WorkerRun? run = await db.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.BranchCommitCount.ShouldBe(0);
    }

    /// <summary>
    /// Returns the container as always-running; no logs so log-based activity is not triggered.
    /// </summary>
    private sealed class RunningContainerOrchestrator : IWorkerOrchestrator
    {
        private static readonly WorkerStatus RunningStatus =
            new(IsRunning: true, ExitCode: null, FinishedAt: null);

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<ContainerId>> StartAsync(
            WorkerContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in integration tests")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(RunningStatus));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(
            string containerId,
            int tailLines,
            CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Returns <paramref name="firstSummary"/> on the first call, then NotFound on all subsequent
    /// calls — simulates a branch that existed, had commits, then was deleted.
    /// </summary>
    private sealed class SuccessThenNotFoundProviderQueries(BranchCommitSummary firstSummary)
        : IPostExitProviderQueries
    {
        private int _callCount;

        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            if (_callCount == 0)
            {
                _callCount++;
                return Task.FromResult(Result<BranchCommitSummary>.Ok(firstSummary));
            }

            _callCount++;
            return Task.FromResult(
                Result<BranchCommitSummary>.Fail(
                    new Error("Provider.NotFound", "Branch not found") { Kind = ErrorKind.NotFound }));
        }
    }
}
