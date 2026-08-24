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
/// Pre-ship integration test: when GetBranchCommitSummaryAsync returns a non-NotFound failure,
/// ObserveRunningWorkerAsync leaves BranchCommitCount unchanged (no commit-path broadcast),
/// while still emitting a container-alive broadcast from RecordActivity,
/// verified through the real module DI wiring.
/// </summary>
public sealed class WhenProviderReturnsTransientError : IAsyncDisposable
{
    private const int InitialCommitCount = 2;
    private const string InitialSha = "sha-before-error";

    private readonly CapturingBroadcaster _broadcaster;
    private readonly FoundryWebAppFactory _factory;

    public WhenProviderReturnsTransientError()
    {
        _broadcaster = new CapturingBroadcaster();

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IWorkerOrchestrator>();
            services.AddSingleton<IWorkerOrchestrator>(new RunningContainerOrchestrator());

            services.RemoveAll<IPostExitProviderQueries>();
            services.AddSingleton<IPostExitProviderQueries>(
                new TransientErrorAfterSuccessProviderQueries(
                    new BranchCommitSummary(InitialCommitCount, InitialSha)));

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
            ContainerId.From("container-transient-error"),
            BranchName.From("feat/99-transient-error"),
            MonitoredRepositoryId.New());
        dbContext.Set<WorkerRun>().Add(activeRun);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenProviderReturnsTransientError_BranchCommitCountUnchangedAndNoCommitBroadcastEmitted()
    {
        // Arrange
        await SeedActiveRunAsync();

        IServiceScopeFactory scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        using WorkerDispatchService sut = new(scopeFactory, NullLogger<WorkerDispatchService>.Instance, TimeProvider.System);

        // Act — Tick 1: provider returns the initial commit summary (count = 2);
        //       RecordActivity fires (container-alive) + RecordBranchCommitCount fires (new SHA).
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Capture broadcast count after tick 1
        int broadcastsAfterTick1 = _broadcaster.Broadcasts.Count;

        // Act — Tick 2: provider returns a transient (non-NotFound) error;
        //       RecordActivity fires (container-alive, 1 broadcast), RecordBranchCommitCount does NOT fire.
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — persisted count is unchanged from tick 1
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext db = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        WorkerRun? run = await db.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.BranchCommitCount.ShouldBe(InitialCommitCount);

        // Assert — exactly one new broadcast on tick 2 (from RecordActivity/container-alive),
        //          and that broadcast carries the unchanged commit count (not a commit-path event).
        int broadcastsAfterTick2 = _broadcaster.Broadcasts.Count;
        (broadcastsAfterTick2 - broadcastsAfterTick1).ShouldBe(
            1, "container-alive fires exactly one broadcast per tick");
        _broadcaster.Broadcasts[broadcastsAfterTick1].CommitCount.ShouldBe(
            InitialCommitCount, "broadcast carries the unchanged commit count, not a new commit-path value");
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
    /// Returns <paramref name="firstSummary"/> on the first call, then a transient (non-NotFound)
    /// error on all subsequent calls — simulates an initial success followed by a provider outage.
    /// </summary>
    private sealed class TransientErrorAfterSuccessProviderQueries(BranchCommitSummary firstSummary)
        : IPostExitProviderQueries
    {
        private int _callCount;

        public Task<Result<bool>> CreateBranchAsync(
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
                    new Error("Provider.Unavailable", "provider transient failure")));
        }
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
