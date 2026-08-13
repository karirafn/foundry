using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using DomainWorkerRunFailed = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

/// <summary>
/// Verifies that Workers module dispatch sites write their integration events to outbox_messages
/// atomically via the outbox interceptor (enqueue-before-save pattern).
/// </summary>
public sealed class OutboxHarvestDispatch : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public OutboxHarvestDispatch()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(_connection);

        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ServiceProvider BuildServiceProvider(SqliteConnection connection)
    {
        ServiceCollection services = new();

        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();
        services.AddScoped<IIntegrationEventProcessor, NullIntegrationEventProcessor>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IDomainEventHandler<DomainWorkerRunFailed>, WorkerRunFailedBridgeHandler>();

        services.AddScoped<IGlobalSettingsQueries, StubGlobalSettingsQueries>();
        services.AddScoped<IPostExitProviderQueries, NullPostExitProviderQueries>();
        services.AddSingleton<IContainerOutputParser, NullContainerOutputParser>();
        services.AddScoped<WorkerOutcomeResolver>(sp => new WorkerOutcomeResolver(
            sp.GetRequiredService<IPostExitProviderQueries>(),
            sp.GetRequiredService<IContainerOutputParser>(),
            prRetryDelay: TimeSpan.Zero));
        services.AddScoped<ICredentialGate, AlwaysCanDispatchCredentialGate>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task WhenContainerCompletesSuccessfully_WorkerRunCompletedRowPersistedToOutbox()
    {
        // Arrange — seed an ActiveRun; orchestrator returns exited-with-zero status and a
        // GetMergeRequestByBranch result of "Open" so outcome resolves to Completed.
        ActiveRun activeRun = SeedActiveRun("container-harvest-complete");

        using (IServiceScope scope = _serviceProvider.CreateScope())
        {
            FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            db.Set<WorkerRun>().Add(activeRun);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        IWorkerOrchestrator orchestrator = new ExitedOrchestrator(exitCode: 0);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — WorkerRunCompleted enqueued before TransitionAsync; harvest save wrote it
        using IServiceScope assertScope = _serviceProvider.CreateScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> messages = await assertDb.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(WorkerRunCompleted)));
    }

    [Fact]
    public async Task WhenAutoResumeOccurs_DispatchResumedRowPersistedToOutbox()
    {
        // Arrange — seed GlobalSettings with expired usage-limit reset time and auto-resume enabled
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            GlobalSettings settings = GlobalSettings.Create();
            settings.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(-1));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Force UsageLimitResetsAt to a past time in the DB (domain logic caps to future)
            GlobalSettings? saved = await seedDb.Set<GlobalSettings>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            saved.ShouldNotBeNull();
            seedDb.Entry(saved).Property("UsageLimitResetsAt").CurrentValue =
                DateTimeOffset.UtcNow.AddHours(-1);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        IWorkerOrchestrator orchestrator = new NullOrchestrator();
        WorkerDispatchService sut = BuildService(orchestrator, usePastResetsAt: true);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — DispatchResumed enqueued before SaveChanges; harvest save wrote it
        using IServiceScope assertScope = _serviceProvider.CreateScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> messages = await assertDb.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(DispatchResumed)));
    }

    [Fact]
    public async Task WhenTickDispatchesCapacityAvailable_WorkerCapacityAvailableRowPersistedToOutbox()
    {
        // Arrange — no active runs; tick reaches capacity-available dispatch, then SaveChangesAsync
        IWorkerOrchestrator orchestrator = new NullOrchestrator();
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — WorkerCapacityAvailable enqueued then SaveChangesAsync harvested it
        using IServiceScope assertScope = _serviceProvider.CreateScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> messages = await assertDb.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(WorkerCapacityAvailable)));
    }

    [Fact]
    public async Task WhenContainerExitsWithAuthInvalidReason_WorkerAuthenticationFailedRowPersistedToOutbox()
    {
        // Arrange — seed an ActiveRun; orchestrator returns exited-with-auth-invalid output
        ActiveRun activeRun = SeedActiveRun("container-harvest-auth-fail");

        using (IServiceScope scope = _serviceProvider.CreateScope())
        {
            FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            db.Set<WorkerRun>().Add(activeRun);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        IWorkerOrchestrator orchestrator = new ExitedOrchestrator(
            exitCode: 1,
            containerOutput: AuthInvalidOutput);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — WorkerAuthenticationFailed enqueued then SaveChangesAsync harvested it
        using IServiceScope assertScope = _serviceProvider.CreateScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> messages = await assertDb.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(WorkerAuthenticationFailed)));
    }

    [Fact]
    public async Task WhenContainerExitsWithCreditsExhaustedReason_WorkerCreditsExhaustedRowPersistedToOutbox()
    {
        // Arrange — seed an ActiveRun; orchestrator returns exited-with-credits-exhausted output
        ActiveRun activeRun = SeedActiveRun("container-harvest-credits-exhausted");

        using (IServiceScope scope = _serviceProvider.CreateScope())
        {
            FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            db.Set<WorkerRun>().Add(activeRun);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        IWorkerOrchestrator orchestrator = new ExitedOrchestrator(
            exitCode: 1,
            containerOutput: Credits429OnlyOutput);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — WorkerCreditsExhausted enqueued then SaveChangesAsync harvested it
        using IServiceScope assertScope = _serviceProvider.CreateScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> messages = await assertDb.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(WorkerCreditsExhausted)));
    }

    private const string AuthInvalidOutput =
        """
        Some output
        {"api_error_status":401}
        """;

    private const string Credits429OnlyOutput =
        """
        Some output
        {"api_error_status":429}
        """;

    private WorkerDispatchService BuildService(
        IWorkerOrchestrator orchestrator,
        bool usePastResetsAt = false)
    {
        SqliteConnection connection = _connection;

        ServiceCollection services = new();
        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();
        services.AddScoped<IIntegrationEventProcessor, NullIntegrationEventProcessor>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IDomainEventHandler<DomainWorkerRunFailed>, WorkerRunFailedBridgeHandler>();

        services.AddScoped<IGlobalSettingsQueries>(sp =>
            new StubGlobalSettingsQueries(usePastResetsAt ? DateTimeOffset.UtcNow.AddHours(-1) : null));
        services.AddScoped<IPostExitProviderQueries, NullPostExitProviderQueries>();
        services.AddSingleton<IContainerOutputParser>(new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance));
        services.AddScoped<WorkerOutcomeResolver>(sp => new WorkerOutcomeResolver(
            sp.GetRequiredService<IPostExitProviderQueries>(),
            sp.GetRequiredService<IContainerOutputParser>(),
            prRetryDelay: TimeSpan.Zero));
        services.AddScoped<ICredentialGate, AlwaysCanDispatchCredentialGate>();
        services.AddSingleton<IWorkerOrchestrator>(_ => orchestrator);

        ServiceProvider sp = services.BuildServiceProvider();
        return new WorkerDispatchService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkerDispatchService>.Instance);
    }

    private static ActiveRun SeedActiveRun(string containerId)
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        return starting.Activate(
            ContainerId.From(containerId),
            BranchName.From("feat/1-test"),
            MonitoredRepositoryId.New());
    }

    // ─── Stubs ───────────────────────────────────────────────────────────────────

    private sealed class StubGlobalSettingsQueries(DateTimeOffset? usageLimitResetsAt = null)
        : IGlobalSettingsQueries
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
            => Task.FromResult(new DispatchPauseState(
                usageLimitResetsAt,
                IsDispatchPaused: false,
                AutoResumeOnUsageReset: true));

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken)
            => Task.FromResult(WorkerImageConfiguration.Default.ToBuildArgs());
    }

    private sealed class NullPostExitProviderQueries : IPostExitProviderQueries
    {
        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<MergeRequestByBranch>.Ok(
                new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit")));
    }

    private sealed class NullContainerOutputParser : IContainerOutputParser
    {
        public ContainerOutputParseResult Parse(string? log)
            => new ContainerOutputParseResult.NormalExit();

        public RunResultSummary? ParseRunResultSummary(string? log) => null;
    }

    private sealed class AlwaysCanDispatchCredentialGate : ICredentialGate
    {
        public Task<bool> CanDispatchAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class NullIntegrationEventProcessor : IIntegrationEventProcessor
    {
        public Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class ExitedOrchestrator(
        int exitCode,
        string? containerOutput = null) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(
                new WorkerStatusProbe.Available(
                    new WorkerStatus(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow)));

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
            => Task.FromResult(containerOutput);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

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
}
