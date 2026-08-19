using System.Runtime.CompilerServices;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using DomainWorkerRunFailed = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.StaleStartingRunServiceTests;

#pragma warning disable CA1001 // StaleStartingRunServiceTestBase implements IAsyncDisposable for connection cleanup.

public abstract class StaleStartingRunServiceTestBase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    protected StaleStartingRunServiceTestBase()
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

    /// <summary>
    /// Seeds a <see cref="StartingRun"/> and then back-dates its <c>created_at</c> column via
    /// raw SQL so tests can exercise the staleness threshold without a test-only setter.
    /// </summary>
    internal async Task<StartingRun> SeedStaleStartingRunAsync(TimeSpan ageOffset)
    {
        StartingRun starting = await SeedStartingRunAsync();
        DateTimeOffset backdatedAt = DateTimeOffset.UtcNow - ageOffset;
        await using FoundryDbContext db = CreateDbContext();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE worker_runs SET created_at = {backdatedAt:O} WHERE id = {starting.Id.Value}",
            CancellationToken.None);
        return starting;
    }

    internal async Task<StartingRun> SeedStartingRunAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        db.Set<WorkerRun>().Add(starting);
        await db.SaveChangesAsync(CancellationToken.None);
        return starting;
    }

    internal async Task<ActiveRun> SeedActiveRunAsync(string containerId = "container-123")
    {
        await using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From(containerId),
            BranchName.From("feat/1-default"),
            MonitoredRepositoryId.New());
        db.Set<WorkerRun>().Add(active);
        await db.SaveChangesAsync(CancellationToken.None);
        return active;
    }

    internal StaleStartingRunService BuildService(
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher? integrationEventDispatcher = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
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

        if (domainEventDispatcher is not null)
        {
            services.AddScoped<IDomainEventDispatcher>(_ => domainEventDispatcher);
        }
        else
        {
            services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        }

        services.AddScoped<IDomainEventHandler<DomainWorkerRunFailed>, WorkerRunFailedBridgeHandler>();
        services.AddScoped<IIntegrationEventDispatcher>(
            _ => integrationEventDispatcher ?? new NullIntegrationEventDispatcher());
        services.AddScoped<IWorkerOrchestrator>(_ => orchestrator);

        ServiceProvider sp = services.BuildServiceProvider();

        return new StaleStartingRunService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleStartingRunService>.Instance);
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

    /// <summary>
    /// A scriptable stub for <see cref="IWorkerOrchestrator"/> covering the subset of operations
    /// used by <see cref="StaleStartingRunService"/>: <c>ListByLabelAsync</c> and
    /// <c>StopAndRemoveAsync</c>.
    /// </summary>
    internal sealed class OrchestratorStub : IWorkerOrchestrator
    {
        private readonly IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)> _containers;
        private readonly bool _listThrows;
        private readonly bool _listThrowsConnectivity;

        public int StopAndRemoveCallCount { get; private set; }

        public List<string> StoppedAndRemovedContainerIds { get; } = [];

        public OrchestratorStub(
            IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>? containers = null,
            bool listThrows = false)
        {
            _containers = containers ?? [];
            _listThrows = listThrows;
        }

        private OrchestratorStub(
            IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)> containers,
            bool listThrows,
            bool listThrowsConnectivity)
        {
            _containers = containers;
            _listThrows = listThrows;
            _listThrowsConnectivity = listThrowsConnectivity;
        }

        public static OrchestratorStub WithConnectivityException(
            IReadOnlyList<(ContainerId, WorkerRunId)>? containers = null)
            => new(containers ?? [], listThrows: false, listThrowsConnectivity: true);

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
        {
            if (_listThrowsConnectivity)
            {
                throw new HttpRequestException("Docker daemon connection refused");
            }

            if (_listThrows)
            {
                throw new InvalidOperationException("Docker daemon unavailable");
            }

            return Task.FromResult(_containers);
        }

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        {
            StopAndRemoveCallCount++;
            StoppedAndRemovedContainerIds.Add(containerId);
            return Task.CompletedTask;
        }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test", "Not supported")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.NotFound());

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
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
