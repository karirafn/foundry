using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

using DomainWorkerRunFailed = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

namespace Foundry.UnitTests.Modules.Workers.Features.IssueClaimedHandlerTests;

/// <summary>
/// Tests the end-to-end pipeline: pre-container failures in IssueClaimedHandler raise the
/// domain WorkerRunFailed event → WorkerRunFailedBridgeHandler converts it to the integration
/// event → integration event is dispatched exactly once.
/// </summary>
public sealed class PreContainerFailureBridging : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private ServiceProvider? _serviceProvider;
    private IServiceScope? _serviceScope;

    public PreContainerFailureBridging()
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
        if (_serviceScope is IDisposable scopeDisposable)
        {
            scopeDisposable.Dispose();
        }

        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
    }

    private IssueClaimedHandler BuildHandler(
        CapturingIntegrationEventDispatcher integrationEventDispatcher,
        IWorkerOrchestrator? orchestrator = null,
        IPostExitProviderQueries? postExitProviderQueries = null)
    {
        ServiceCollection services = new();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IDomainEventHandler<DomainWorkerRunFailed>, WorkerRunFailedBridgeHandler>();
        services.AddScoped<IIntegrationEventDispatcher>(_ => integrationEventDispatcher);
        _serviceProvider = services.BuildServiceProvider();
        _serviceScope = _serviceProvider.CreateScope();

        IDomainEventDispatcher domainDispatcher =
            _serviceScope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        return new IssueClaimedHandler(
            _dbContext,
            orchestrator ?? new AlwaysFailingOrchestrator(),
            domainDispatcher,
            Options.Create(new WorkerOptions { Image = "test-image:latest" }),
            new StubGlobalSettingsQueries(),
            new StubCredentialQueries(("ANTHROPIC_API_KEY", "test-key")),
            postExitProviderQueries ?? new BranchCreationFailsQueries(),
            NullLogger<IssueClaimedHandler>.Instance);
    }

    private static IssueClaimed BuildEvent(IssueId? issueId = null)
    {
        ClaimedIssueDispatch dispatch = new(
            issueId ?? IssueId.New(),
            Guid.NewGuid(),
            1,
            "Test",
            "Test body",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "ghp_test",
            "feat/1-test",
            MonitoredRepositoryId.New(),
            new WorkerProvider.GitHub());
        return new IssueClaimed(dispatch);
    }

    [Fact]
    public async Task WhenBranchPreCreationFails_PublishesWorkerRunFailedIntegrationEventExactlyOnceWithNullBranch()
    {
        // Arrange — branch pre-creation failure is always terminal (null branch)
        CapturingIntegrationEventDispatcher integrationEventDispatcher = new();
        IssueClaimedHandler sut = BuildHandler(
            integrationEventDispatcher,
            postExitProviderQueries: new BranchCreationFailsQueries());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        Foundry.Modules.Workers.Contracts.WorkerRunFailed failedEvent =
            integrationEventDispatcher.Captured
                .OfType<Foundry.Modules.Workers.Contracts.WorkerRunFailed>()
                .ShouldHaveSingleItem();
        failedEvent.ShouldSatisfyAllConditions(
            () => failedEvent.BranchName.ShouldBeNull());
    }

    [Fact]
    public async Task WhenBranchPreCreationFails_ProviderErrorSummaryPassesThroughErrorMessage()
    {
        // Arrange
        CapturingIntegrationEventDispatcher integrationEventDispatcher = new();
        IssueClaimedHandler sut = BuildHandler(
            integrationEventDispatcher,
            postExitProviderQueries: new BranchCreationFailsQueries());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ProviderError providerError = failedRun.Reason.ShouldBeOfType<FailureReason.ProviderError>();
        providerError.Message.ShouldBe("403 Forbidden");
        providerError.Summary.ShouldBe("403 Forbidden");
    }

    [Fact]
    public async Task WhenContainerStartFails_PersistedReasonIsContainerError()
    {
        // Arrange — container-start failure must still map to ContainerError (not ProviderError)
        CapturingIntegrationEventDispatcher integrationEventDispatcher = new();
        IssueClaimedHandler sut = BuildHandler(
            integrationEventDispatcher,
            orchestrator: new AlwaysFailingOrchestrator(),
            postExitProviderQueries: new BranchCreationSucceedsQueries());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
    }

    [Fact]
    public async Task WhenContainerStartFails_PublishesWorkerRunFailedIntegrationEventExactlyOnceWithNullBranch()
    {
        // Arrange — container-start failure is pre-container (always terminal, null branch)
        CapturingIntegrationEventDispatcher integrationEventDispatcher = new();
        IssueClaimedHandler sut = BuildHandler(
            integrationEventDispatcher,
            orchestrator: new AlwaysFailingOrchestrator(),
            postExitProviderQueries: new BranchCreationSucceedsQueries());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        Foundry.Modules.Workers.Contracts.WorkerRunFailed failedEvent =
            integrationEventDispatcher.Captured
                .OfType<Foundry.Modules.Workers.Contracts.WorkerRunFailed>()
                .ShouldHaveSingleItem();
        failedEvent.ShouldSatisfyAllConditions(
            () => failedEvent.BranchName.ShouldBeNull());
    }

    private sealed class AlwaysFailingOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Orchestrator.Failed", "Docker daemon unreachable")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.NotFound());

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class BranchCreationFailsQueries : IPostExitProviderQueries
    {
        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<bool>.Fail(new Error("Provider.BranchCreationFailed", "403 Forbidden")));

        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }

    private sealed class BranchCreationSucceedsQueries : IPostExitProviderQueries
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
            => Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }

    private sealed class StubCredentialQueries(
        (string Key, string Value)? authVar) : ICredentialQueries
    {
        public Task<string?> GetAuthModeAsync(CancellationToken cancellationToken)
            => Task.FromResult<string?>(authVar is not null ? "ApiKey" : "OAuth");

        public Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(CancellationToken cancellationToken)
            => Task.FromResult(authVar);

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
    }
}
