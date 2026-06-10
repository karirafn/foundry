using Foundry.Modules.Issues.Contracts;
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
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

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

    internal ActiveRun SeedActiveRun(string containerId = "container-123")
    {
        using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun activeRun = starting.Activate(ContainerId.From(containerId));
        db.Set<WorkerRun>().Add(activeRun);
        db.SaveChanges();
        return activeRun;
    }

    internal WorkerDispatchService BuildService(
        IWorkerOrchestrator orchestrator,
        WorkerOptions? workerOptions = null,
        IIntegrationEventDispatcher? integrationEventDispatcher = null,
        IProviderAuth? providerAuth = null,
        IWorkerLogBroadcaster? broadcaster = null)
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
        services.AddScoped<IProviderAuth>(_ => providerAuth ?? new StubProviderAuth("test-token"));
        services.AddScoped<IWorkerLogBroadcaster>(_ => broadcaster ?? new NullWorkerLogBroadcaster());

        ServiceProvider sp = services.BuildServiceProvider();

        WorkerOptions options = workerOptions ?? new WorkerOptions
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
            ApiKey = "test-api-key",
            TimeoutMinutes = 120,
        };

        return new WorkerDispatchService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<WorkerDispatchService>.Instance);
    }

    protected sealed class StubProviderAuth(string token) : IProviderAuth
    {
        public Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(token));
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

    protected sealed class NullWorkerLogBroadcaster : IWorkerLogBroadcaster
    {
        public Task PushAsync(Guid issueId, WorkerReportSummary report, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
