using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.IssueClaimedHandlerTests;

public sealed class RecordsSuppliedOpenIssues : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public RecordsSuppliedOpenIssues()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenRunIsDispatched_ActiveRunHoldsTheTrackerContextsOpenIssues()
    {
        // Arrange
        TrackerContext trackerContext = new TrackerContextBuilder()
            .WithOpenIssueNumbers(7, 12, 31)
            .Build();
        IssueClaimedHandler sut = BuildHandler(trackerContext);
        IssueClaimed @event = BuildEvent(WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        ActiveRun run = _dbContext.Set<WorkerRun>()
            .OfType<ActiveRun>()
            .Single();
        run.SuppliedOpenIssueNumbers.ShouldBe([7, 12, 31]);
    }

    private IssueClaimedHandler BuildHandler(TrackerContext trackerContext)
    {
        return new IssueClaimedHandler(
            _dbContext,
            new StubWorkerOrchestrator(),
            new FakePublisher(),
            new NullDomainEventDispatcher(),
            new StubGlobalSettingsQueries(),
            new StubCredentialQueries(),
            new StubPostExitProviderQueries(),
            new StubFailingJobLogQueries(),
            StubWorkerRoleCatalogStore.With(),
            StubRepositoryDispatchQueries.WithToken("ghp_test_token"),
            new StubIssueContentQueries(),
            new StubTrackerContextQueries(trackerContext),
            NullLogger<IssueClaimedHandler>.Instance);
    }

    private static IssueClaimed BuildEvent(WorkerRunId workerRunId)
    {
        ClaimedIssueDispatch dispatch = new(
            IssueId.New(),
            workerRunId,
            42,
            "Test Issue",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            BranchName.From("feat/42-test-issue"),
            MonitoredRepositoryId.New(),
            new WorkerProvider.GitHub(),
            new DispatchContext.Fresh("feat/42-test-issue"),
            "https://api.github.com/repos/owner/repo/issues/42");
        return new IssueClaimed(dispatch);
    }

    private sealed class StubWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("container-default")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(
                new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null)));

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

        public Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubCredentialQueries : ICredentialQueries
    {
        public Task<string?> GetAuthModeAsync(CancellationToken cancellationToken)
            => Task.FromResult<string?>("ApiKey");

        public Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(CancellationToken cancellationToken)
            => Task.FromResult<(string Key, string Value)?>(("ANTHROPIC_API_KEY", "test-api-key"));

        public Task<bool> IsValidAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<ClaudeAccountSummary?> GetSummaryAsync(CancellationToken cancellationToken)
            => Task.FromResult<ClaudeAccountSummary?>(null);
    }

    private sealed class StubGlobalSettingsQueries : IGlobalSettingsQueries
    {
        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult<GlobalSettingsSummary?>(null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(3);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(120);

        public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken)
            => Task.FromResult(30);

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DispatchPauseState(null, false, true));

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> GetWorkerImageInstallsDotnetAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken)
            => Task.FromResult(string.Empty);

        public Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<WorkerResourceLimitsSnapshot> GetWorkerResourceLimitsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new WorkerResourceLimitsSnapshot(8192, 2.0, 512));

        public Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ClaudeCodeSettingsSnapshot([]));

        public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class StubPostExitProviderQueries : IPostExitProviderQueries
    {
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
            => Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }
}
