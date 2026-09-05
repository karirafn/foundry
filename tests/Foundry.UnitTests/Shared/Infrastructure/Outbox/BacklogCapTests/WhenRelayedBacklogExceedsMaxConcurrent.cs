using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

using DomainWorkerRunFailed = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.BacklogCapTests;

/// <summary>
/// Headline acceptance criterion for #416: proves that the reservation-as-occupancy
/// mechanism prevents a relayed backlog from over-dispatching past max_concurrent.
///
/// <para>
/// The scenario under test: the dispatch service ticks N times while no claim has
/// completed. Without reservations, each tick sees occupancy = 0 and publishes a fresh
/// <see cref="WorkerCapacityAvailable"/> event — the relay then delivers N events and
/// claims N issues. With reservations, the first tick publishes one event AND persists
/// one <see cref="DispatchReservation"/>. Every subsequent tick sees occupancy = 1
/// (the outstanding reservation) and therefore publishes nothing. Only one event ever
/// reaches the relay, and only one issue is ever claimed.
/// </para>
/// </summary>
public sealed class WhenRelayedBacklogExceedsMaxConcurrent : IAsyncDisposable
{
    private const int MaxConcurrent = 1;
    private const int BacklogSize = 5;

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public WhenRelayedBacklogExceedsMaxConcurrent()
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

        // ─── Outbox infrastructure ────────────────────────────────────────────────
        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();
        services.AddScoped<IIntegrationEventProcessor, IntegrationEventProcessor>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddSingleton(Options.Create(new OutboxOptions()));

        // ─── Domain event dispatching ─────────────────────────────────────────────
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IDomainEventHandler<DomainWorkerRunFailed>, WorkerRunFailedBridgeHandler>();

        // ─── Issues module — WorkerCapacityAvailable handler ─────────────────────
        services.AddScoped<DispatchCandidateSelector>();
        services.AddScoped<IssueClaimer>();
        services.AddScoped<IRepositoryEligibilityQuery, AllEligibleRepositoryEligibilityQuery>();
        services.AddScoped<IRepositoryDispatchQueries, StubRepositoryDispatchQueries>();
        services.AddIntegrationEventHandler<WorkerCapacityAvailable, WorkerCapacityAvailableHandler>();

        // ─── Workers module — dispatch service ───────────────────────────────────
        services.AddScoped<IGlobalSettingsQueries>(_ => new StubGlobalSettingsQueries(MaxConcurrent));
        services.AddScoped<IPostExitProviderQueries, NullPostExitProviderQueries>();
        services.AddSingleton<IContainerOutputParser, NullContainerOutputParser>();
        services.AddScoped<WorkerOutcomeResolver>(sp => new WorkerOutcomeResolver(
            sp.GetRequiredService<IPostExitProviderQueries>(),
            sp.GetRequiredService<IContainerOutputParser>(),
            prRetryDelay: TimeSpan.Zero));
        services.AddScoped<ICredentialGate, AlwaysCanDispatchCredentialGate>();
        services.AddSingleton<IWorkerOrchestrator, NullWorkerOrchestrator>();

        return services.BuildServiceProvider();
    }

    private WorkerDispatchService BuildDispatchService()
        => new(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkerDispatchService>.Instance,
            TimeProvider.System);

    private OutboxRelayService BuildRelayService()
        => new(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<IOptions<OutboxOptions>>(),
            NullLogger<OutboxRelayService>.Instance);

    private async Task SeedQueuedIssueAsync(int issueNumber)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .FreshQueued();

        db.Set<Issue>().Add(queued);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenDispatchTicksMultipleTimes_ReservationCapsWorkerCapacityAvailableToMaxConcurrent()
    {
        // Arrange — seed BacklogSize eligible queued issues so the dispatch service has work to do.
        for (int i = 1; i <= BacklogSize; i++)
        {
            await SeedQueuedIssueAsync(i);
        }

        WorkerDispatchService dispatchService = BuildDispatchService();

        // Act — tick the dispatch service BacklogSize times, simulating a burst of ticks
        // before any prior WorkerCapacityAvailable is processed.
        // Without reservations: each tick sees occupancy = 0 and publishes, producing
        // BacklogSize events. With reservations: the first tick publishes and creates
        // a reservation; subsequent ticks see occupancy = 1 (the reservation) and publish nothing.
        for (int i = 0; i < BacklogSize; i++)
        {
            await dispatchService.ExecuteTickAsync(TestContext.Current.CancellationToken);
        }

        // Assert — only MaxConcurrent outbox row(s) were written despite BacklogSize ticks.
        await using AsyncServiceScope assertScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        List<OutboxMessage> outboxMessages = await assertDb.Set<OutboxMessage>()
            .Where(m => m.Type.Contains(nameof(WorkerCapacityAvailable)))
            .ToListAsync(TestContext.Current.CancellationToken);

        outboxMessages.Count.ShouldBe(MaxConcurrent);
    }

    [Fact]
    public async Task WhenBacklogRelayed_ClaimedIssueCountNeverExceedsMaxConcurrent()
    {
        // Arrange — seed BacklogSize eligible queued issues.
        for (int i = 1; i <= BacklogSize; i++)
        {
            await SeedQueuedIssueAsync(i);
        }

        WorkerDispatchService dispatchService = BuildDispatchService();
        OutboxRelayService relayService = BuildRelayService();

        // Act — tick dispatch BacklogSize times to simulate accumulated backlog pressure,
        // then relay all pending outbox messages.
        for (int i = 0; i < BacklogSize; i++)
        {
            await dispatchService.ExecuteTickAsync(TestContext.Current.CancellationToken);
        }

        await relayService.TickForTest(TestContext.Current.CancellationToken);

        // Assert — at most MaxConcurrent issues transitioned to InProgress.
        // Without the reservation mechanism, all BacklogSize issues would be claimed.
        await using AsyncServiceScope assertScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        int inProgressCount = await assertDb.Set<InProgressIssue>()
            .CountAsync(TestContext.Current.CancellationToken);

        inProgressCount.ShouldBeLessThanOrEqualTo(MaxConcurrent);
    }

    // ─── Stubs ───────────────────────────────────────────────────────────────────

    private sealed class AllEligibleRepositoryEligibilityQuery : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<EligibleRepository> eligible = repositoryIds
                .Select(id => new EligibleRepository(id, Position: 0))
                .ToList();
            return Task.FromResult(eligible);
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class StubRepositoryDispatchQueries : IRepositoryDispatchQueries
    {
        private static readonly RepositoryDispatchInfo DefaultInfo = new(
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "GITHUB_PAT",
            new WorkerProvider.GitHub());

        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryDispatchInfo?>(DefaultInfo);
    }

    private sealed class StubGlobalSettingsQueries(int maxConcurrent) : IGlobalSettingsQueries
    {
        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult<GlobalSettingsSummary?>(null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(maxConcurrent);

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
            => Task.FromResult(WorkerImageConfiguration.Default.ToBuildArgs());

        public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NullPostExitProviderQueries : IPostExitProviderQueries
    {
        public Task<Result<bool>> CreateBranchAsync(
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
            => Task.FromResult(Result<BranchCommitSummary>.Ok(new BranchCommitSummary(0, null)));
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

    private sealed class NullWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(
            WorkerContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in backlog-cap test")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.NotFound());

        public async System.Collections.Generic.IAsyncEnumerable<string> StreamLogsAsync(
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
}
