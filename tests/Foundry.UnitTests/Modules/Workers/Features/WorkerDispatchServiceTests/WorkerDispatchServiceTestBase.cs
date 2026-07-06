using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

#pragma warning disable CA1001 // WorkerDispatchServiceTestBase implements IAsyncDisposable for connection cleanup.

public abstract class WorkerDispatchServiceTestBase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    protected WorkerDispatchServiceTestBase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

    internal FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    internal ActiveRun SeedActiveRun(
        string containerId = "container-123",
        string branchName = "feat/1-default",
        MonitoredRepositoryId? monitoredRepositoryId = null)
    {
        using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun activeRun = starting.Activate(
            ContainerId.From(containerId),
            BranchName.From(branchName),
            monitoredRepositoryId ?? MonitoredRepositoryId.New());
        db.Set<WorkerRun>().Add(activeRun);
        db.SaveChanges();
        return activeRun;
    }

    internal WorkerDispatchService BuildService(
        IWorkerOrchestrator orchestrator,
        WorkerOptions? workerOptions = null,
        IIntegrationEventDispatcher? integrationEventDispatcher = null,
        IGlobalSettingsQueries? settingsQueries = null,
        IPostExitProviderQueries? postExitProviderQueries = null,
        IContainerOutputParser? containerOutputParser = null,
        ICredentialGate? credentialGate = null)
    {
        SqliteConnection connection = _connection;

        ServiceCollection services = new();
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(options);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddScoped<IDomainEventDispatcher, NullDomainEventDispatcher>();
        services.AddScoped<IIntegrationEventDispatcher>(
            _ => integrationEventDispatcher ?? new NullIntegrationEventDispatcher());
        services.AddScoped<IWorkerOrchestrator>(_ => orchestrator);
        services.AddScoped<IGlobalSettingsQueries>(
            _ => settingsQueries ?? new StubGlobalSettingsQueries(maxConcurrent: 3, timeoutMinutes: 120));
        services.AddScoped<IPostExitProviderQueries>(
            _ => postExitProviderQueries ?? new StubPostExitProviderQueries(hasCommits: false));
        services.AddSingleton<IContainerOutputParser>(
            _ => containerOutputParser ?? new NullContainerOutputParser());

        // Register WorkerOutcomeResolver with zero PR retry delay so unit tests do not sleep.
        services.AddScoped<WorkerOutcomeResolver>(sp => new WorkerOutcomeResolver(
            sp.GetRequiredService<IPostExitProviderQueries>(),
            sp.GetRequiredService<IContainerOutputParser>(),
            prRetryDelay: TimeSpan.Zero));

        services.AddScoped<ICredentialGate>(_ => credentialGate ?? new AlwaysCanDispatchCredentialGate());

        ServiceProvider sp = services.BuildServiceProvider();

        return new WorkerDispatchService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkerDispatchService>.Instance);
    }

    /// <summary>
    /// Scriptable stub for <see cref="IPostExitProviderQueries"/>.
    /// <para>
    /// <paramref name="hasCommits"/> — result for <see cref="HasBranchCommitsAsync"/>
    /// when no error is scripted.
    /// </para>
    /// <para>
    /// <paramref name="mergeRequest"/> — when set, <see cref="GetMergeRequestByBranchAsync"/>
    /// returns this value; otherwise returns <see cref="MergeRequestPresence.None"/>.
    /// </para>
    /// <para>
    /// <paramref name="mergeRequestError"/> — when set, <see cref="GetMergeRequestByBranchAsync"/>
    /// returns this failure instead.
    /// </para>
    /// <para>
    /// <paramref name="hasCommitsError"/> — when set, <see cref="HasBranchCommitsAsync"/> returns
    /// this failure (use <see cref="ErrorKind.NotFound"/> to signal a deleted branch).
    /// </para>
    /// </summary>
    protected sealed class StubPostExitProviderQueries(
        bool hasCommits = false,
        MergeRequestByBranch? mergeRequest = null,
        Error? mergeRequestError = null,
        Error? hasCommitsError = null) : IPostExitProviderQueries
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
            => hasCommitsError is not null
                ? Task.FromResult(Result<bool>.Fail(hasCommitsError))
                : Task.FromResult(Result<bool>.Ok(hasCommits));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            if (mergeRequestError is not null)
            {
                return Task.FromResult(Result<MergeRequestByBranch>.Fail(mergeRequestError));
            }

            MergeRequestByBranch result = mergeRequest
                ?? new MergeRequestByBranch(MergeRequestPresence.None, null);
            return Task.FromResult(Result<MergeRequestByBranch>.Ok(result));
        }

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }

    protected sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    protected sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    protected sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
    }

    internal sealed class NullContainerOutputParser : IContainerOutputParser
    {
        public ContainerOutputParseResult Parse(string? log, int defaultCooldownMinutes)
            => new ContainerOutputParseResult.NormalExit();

        public RunResultSummary? ParseRunResultSummary(string? log) => null;
    }

    protected sealed class StubGlobalSettingsQueries(int maxConcurrent = 3, int timeoutMinutes = 120)
        : IGlobalSettingsQueries
    {
        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult<GlobalSettingsSummary?>(null);

        public Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(CancellationToken cancellationToken)
            => Task.FromResult<(string Key, string Value)?>(("ANTHROPIC_API_KEY", "test-api-key"));

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(maxConcurrent);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(timeoutMinutes);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DispatchPauseState(null, false, true));

        public Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<string?> GetAuthModeAsync(CancellationToken cancellationToken)
            => Task.FromResult<string?>("ApiKey");
    }

    private sealed class AlwaysCanDispatchCredentialGate : ICredentialGate
    {
        public Task<bool> CanDispatchAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
