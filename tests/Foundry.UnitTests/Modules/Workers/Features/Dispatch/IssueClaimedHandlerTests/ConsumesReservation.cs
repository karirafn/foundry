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
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.IssueClaimedHandlerTests;

public sealed class ConsumesReservation : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public ConsumesReservation()
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

    private IssueClaimedHandler BuildHandler()
    {
        WorkerOptions options = new()
        {
            Image = "test-image:latest",
        };
        return new IssueClaimedHandler(
            _dbContext,
            new StubWorkerOrchestrator(succeeds: true, containerId: "container-default"),
            new NullDomainEventDispatcher(),
            Options.Create(options),
            new StubGlobalSettingsQueries(),
            new StubCredentialQueries(("ANTHROPIC_API_KEY", "test-api-key")),
            new StubPostExitProviderQueries(),
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
            "ghp_test_token",
            BranchName.From("feat/42-test-issue"),
            MonitoredRepositoryId.New(),
            new WorkerProvider.GitHub(),
            new DispatchContext.Fresh("feat/42-test-issue"),
            "https://api.github.com/repos/owner/repo/issues/42");
        return new IssueClaimed(dispatch);
    }

    [Fact]
    public async Task WhenReservationExists_DeletesReservationAndCreatesStartingRun()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        DispatchReservation reservation = new DispatchReservationBuilder()
            .WithWorkerRunId(workerRunId)
            .Build();
        _dbContext.Set<DispatchReservation>().Add(reservation);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        IssueClaimedHandler sut = BuildHandler();
        IssueClaimed @event = BuildEvent(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert — reservation deleted AND run exists (post-conditions of one save)
        _dbContext.ShouldSatisfyAllConditions(
            () => _dbContext.Set<DispatchReservation>()
                .Any()
                .ShouldBeFalse(),
            () => _dbContext.Set<WorkerRun>()
                .Any(r => r.Id == workerRunId)
                .ShouldBeTrue());
    }

    [Fact]
    public async Task WhenNoReservationExists_CreatesStartingRunWithoutError()
    {
        // Arrange — no reservation seeded (redelivery / already swept scenario)
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimedHandler sut = BuildHandler();
        IssueClaimed @event = BuildEvent(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert — run created, no reservation row, no exception thrown
        _dbContext.ShouldSatisfyAllConditions(
            () => _dbContext.Set<DispatchReservation>()
                .Any()
                .ShouldBeFalse(),
            () => _dbContext.Set<WorkerRun>()
                .Any(r => r.Id == workerRunId)
                .ShouldBeTrue());
    }

    private sealed class StubWorkerOrchestrator(bool succeeds, string? containerId = null) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
        {
            Result<ContainerId> result = succeeds
                ? Result<ContainerId>.Ok(ContainerId.From(containerId ?? "default-container"))
                : Result<ContainerId>.Fail(new Error("Orchestrator.StartFailed", "Start failed"));
            return Task.FromResult(result);
        }

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

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubCredentialQueries(
        (string Key, string Value)? authVar,
        bool settingsExist = true) : ICredentialQueries
    {
        public Task<string?> GetAuthModeAsync(CancellationToken cancellationToken)
        {
            if (!settingsExist)
            {
                return Task.FromResult<string?>(null);
            }

            string? mode = authVar is not null ? "ApiKey" : "OAuth";
            return Task.FromResult<string?>(mode);
        }

        public Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(CancellationToken cancellationToken)
            => Task.FromResult(authVar);

        public Task<bool> IsValidAsync(CancellationToken cancellationToken)
            => Task.FromResult(settingsExist);

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

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DispatchPauseState(null, false, true));

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

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
