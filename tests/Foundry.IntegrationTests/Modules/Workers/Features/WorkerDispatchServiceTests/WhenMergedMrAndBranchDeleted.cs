using System.Runtime.CompilerServices;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Features.WorkerDispatchServiceTests;

/// <summary>
/// Pre-ship integration test verifying the MR-state-first outcome path wires correctly through
/// the real module DI. WorkerOutcomeResolver's GetMergeRequestByBranchAsync-first approach
/// correctly resolves the merged-MR + branch-deleted scenario.
/// </summary>
public sealed class WhenMergedMrAndBranchDeleted : IAsyncDisposable
{
    private const string MergedPrUrl = "https://github.com/owner/repo/pull/99";

    private readonly FoundryWebAppFactory _factory;

    public WhenMergedMrAndBranchDeleted()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            // Replace the Docker-backed orchestrator — real implementation needs a live Docker daemon.
            services.RemoveAll<IWorkerOrchestrator>();
            services.AddSingleton<IWorkerOrchestrator>(new ExitedOrchestrator());

            // Replace provider queries — real implementation needs seeded account/repo rows
            // and live provider tokens. Stub the merged MR + branch-deleted scenario directly.
            services.RemoveAll<IPostExitProviderQueries>();
            services.AddScoped<IPostExitProviderQueries>(_ => new MergedMrBranchDeletedQueries(MergedPrUrl));
        });
        _ = _factory.CreateClient(); // triggers ConfigureWebHost + EnsureCreated
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
            ContainerId.From("merged-container"),
            BranchName.From("feat/99-merged"),
            MonitoredRepositoryId.New());
        dbContext.Set<WorkerRun>().Add(activeRun);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenMergedMrAndBranchDeleted_TransitionsToCompletedRunWithPrUrl()
    {
        // Arrange — the real module DI registers WorkerOutcomeResolver as scoped;
        // this test verifies the MR-state-first path works end-to-end through the wired container.
        await SeedActiveRunAsync();

        IServiceScopeFactory scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        using WorkerDispatchService sut = new(scopeFactory, NullLogger<WorkerDispatchService>.Instance);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext db = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        WorkerRun? run = await db.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        CompletedRun completedRun = run.ShouldBeOfType<CompletedRun>();
        completedRun.PullRequestUrl.HasValue.ShouldBeTrue();
        completedRun.PullRequestUrl!.Value.Value.ShouldBe(MergedPrUrl);
    }

    /// <summary>
    /// Stub orchestrator returning an exited container (exit code 0, no logs).
    /// </summary>
    private sealed class ExitedOrchestrator : IWorkerOrchestrator
    {
        private static readonly WorkerStatus ExitedStatus =
            new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<ContainerId>> StartAsync(
            WorkerContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in integration tests")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(ExitedStatus);

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
    /// Stub provider queries for the merged MR + branch-deleted scenario:
    /// <see cref="GetMergeRequestByBranchAsync"/> returns a merged MR with a URL;
    /// <see cref="HasBranchCommitsAsync"/> returns NotFound (branch was deleted after merge).
    /// </summary>
    private sealed class MergedMrBranchDeletedQueries(string prUrl) : IPostExitProviderQueries
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
        {
            // Branch was deleted after merge — provider returns NotFound.
            // The resolver never reaches this path for a Merged MR, but the stub
            // provides an accurate representation of the real scenario.
            Error error = new("Provider.BranchNotFound", "Branch not found")
            {
                Kind = ErrorKind.NotFound,
            };
            return Task.FromResult(Result<bool>.Fail(error));
        }

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            MergeRequestByBranch mergedMr = new(MergeRequestPresence.Merged, prUrl);
            return Task.FromResult(Result<MergeRequestByBranch>.Ok(mergedMr));
        }

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }
}
