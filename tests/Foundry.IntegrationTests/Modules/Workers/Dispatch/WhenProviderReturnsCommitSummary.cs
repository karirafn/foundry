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
/// Pre-ship integration test: when a running worker's provider returns a commit summary,
/// ObserveRunningWorkerAsync persists BranchCommitCount and LastObservedCommitSha to the database
/// through the real module DI wiring.
/// </summary>
public sealed class WhenProviderReturnsCommitSummary : IAsyncDisposable
{
    private const int ExpectedCommitCount = 3;
    private const string ExpectedSha = "sha-integration-abc123";

    private readonly FoundryWebAppFactory _factory;
    private readonly CapturingBroadcaster _broadcaster;

    public WhenProviderReturnsCommitSummary()
    {
        _broadcaster = new CapturingBroadcaster();

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IWorkerOrchestrator>();
            services.AddSingleton<IWorkerOrchestrator>(new RunningContainerOrchestrator());

            services.RemoveAll<IPostExitProviderQueries>();
            services.AddScoped<IPostExitProviderQueries>(_ => new CommitSummaryProviderQueries(
                new BranchCommitSummary(ExpectedCommitCount, ExpectedSha)));

            services.RemoveAll<IWorkerActivityBroadcaster>();
            services.AddScoped<IWorkerActivityBroadcaster>(_ => _broadcaster);
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
            ContainerId.From("container-commit-summary"),
            BranchName.From("feat/99-commit-summary"),
            MonitoredRepositoryId.New());
        dbContext.Set<WorkerRun>().Add(activeRun);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenProviderReturnsCommitSummary_BranchCommitCountAndShaPersistedToDatabase()
    {
        // Arrange
        await SeedActiveRunAsync();

        IServiceScopeFactory scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        using WorkerDispatchService sut = new(scopeFactory, NullLogger<WorkerDispatchService>.Instance);

        // Act — Tick 1: reconciliation (sets _reconciled = true, runs observation on running container)
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: monitoring path, provider returns commit summary
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — BranchCommitCount and LastObservedCommitSha are persisted
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext db = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        WorkerRun? run = await db.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.ShouldSatisfyAllConditions(
            () => activeRun.BranchCommitCount.ShouldBe(ExpectedCommitCount),
            () => activeRun.LastObservedCommitSha.ShouldBe(ExpectedSha));
    }

    [Fact]
    public async Task WhenProviderReturnsCommitSummary_BroadcastsWorkerActivityWithCommitCount()
    {
        // Arrange
        await SeedActiveRunAsync();

        IServiceScopeFactory scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        using WorkerDispatchService sut = new(scopeFactory, NullLogger<WorkerDispatchService>.Instance);

        // Act — Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: monitoring path, provider returns commit summary
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — a WorkerActivity with the expected commit count was broadcast
        _broadcaster.Broadcasts.ShouldContain(a => a.CommitCount == ExpectedCommitCount);
    }

    /// <summary>
    /// Returns the container as always-running; logs return null so log-based activity is not triggered.
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
    /// Always returns the given <see cref="BranchCommitSummary"/> for GetBranchCommitSummaryAsync.
    /// </summary>
    private sealed class CommitSummaryProviderQueries(BranchCommitSummary summary) : IPostExitProviderQueries
    {
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
            => Task.FromResult(Result<BranchCommitSummary>.Ok(summary));
    }

    private sealed class CapturingBroadcaster : IWorkerActivityBroadcaster
    {
        public List<WorkerActivity> Broadcasts { get; } = [];

        public Task BroadcastActivityAsync(WorkerActivity activity, CancellationToken cancellationToken)
        {
            Broadcasts.Add(activity);
            return Task.CompletedTask;
        }
    }
}
