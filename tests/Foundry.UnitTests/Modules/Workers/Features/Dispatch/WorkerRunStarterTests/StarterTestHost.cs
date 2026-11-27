using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerRunStarterTests;

/// <summary>
/// Hosts <see cref="WorkerRunStarter"/> as production does: every scope gets its own <see cref="DbContext"/> on its own
/// connection to one shared in-memory database, so starts can run concurrently.
/// </summary>
internal sealed class StarterTestHost : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<FoundryDbContext> _options;
    private readonly ServiceProvider _services;

    public StarterTestHost(
        IPublisher? publisher = null,
        IIssueContentQueries? issueContentQueries = null,
        IWorkerOrchestrator? orchestrator = null)
    {
        string connectionString = $"Data Source=file:starter-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        _options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(connectionString)
            .Options;

        using (FoundryDbContext setup = CreateDbContext())
        {
            setup.Database.EnsureCreated();
        }

        Publisher = publisher ?? new FakePublisher();
        Orchestrator = orchestrator ?? new HandleAsync.StubWorkerOrchestrator(succeeds: true, containerId: "container-default");

        ServiceCollection services = new();
        services.AddScoped<DbContext>(_ => CreateDbContext());
        services.AddSingleton(Orchestrator);
        services.AddSingleton(Publisher);
        services.AddSingleton<IDomainEventDispatcher>(new NullDomainEventDispatcher());
        services.AddSingleton<IIntegrationEventDispatcher>(new CapturingIntegrationEventDispatcher());
        services.AddSingleton<IGlobalSettingsQueries>(new HandleAsync.StubGlobalSettingsQueries());
        services.AddSingleton<ICredentialQueries>(new HandleAsync.StubCredentialQueries(("ANTHROPIC_API_KEY", "test-api-key")));
        services.AddSingleton<IPostExitProviderQueries>(new HandleAsync.StubPostExitProviderQueries(branchCreationSucceeds: true));
        services.AddSingleton<IFailingJobLogQueries>(new StubFailingJobLogQueries());
        services.AddSingleton<IWorkerRoleCatalogStore>(StubWorkerRoleCatalogStore.With());
        services.AddSingleton<IRepositoryDispatchQueries>(StubRepositoryDispatchQueries.WithToken("ghp_test_token"));
        services.AddSingleton(issueContentQueries ?? new StubIssueContentQueries());
        services.AddSingleton<ITrackerContextQueries>(new StubTrackerContextQueries());
        services.AddSingleton<ISystemNotificationBroadcaster>(new CapturingSystemNotificationBroadcaster());
        services.AddSingleton<IWorkerHostEntryResolver>(new StubWorkerHostEntryResolver());
        services.AddSingleton<ILogger<WorkerRunStart>>(NullLogger<WorkerRunStart>.Instance);
        services.AddSingleton<WorkerRunStartSignal>();
        services.AddScoped<WorkerRunStart>();
        _services = services.BuildServiceProvider();

        Starter = new WorkerRunStarter(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Signal,
            NullLogger<WorkerRunStarter>.Instance);
    }

    public IPublisher Publisher { get; }

    public IWorkerOrchestrator Orchestrator { get; }

    public WorkerRunStartSignal Signal => _services.GetRequiredService<WorkerRunStartSignal>();

    public WorkerRunStarter Starter { get; }

    public FoundryDbContext CreateDbContext() => new(_options);

    /// <summary>Delivers <paramref name="event"/> to <see cref="IssueClaimedHandler"/> in its own scope, as the relay does.</summary>
    public async Task ClaimAsync(IssueClaimed @event, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IssueClaimedHandler handler = new(
            scope.ServiceProvider.GetRequiredService<DbContext>(),
            scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>(),
            scope.ServiceProvider.GetRequiredService<IWorkerRoleCatalogStore>(),
            Signal,
            NullLogger<IssueClaimedHandler>.Instance);
        await handler.HandleAsync(@event, cancellationToken);
    }

    public static IssueClaimed BuildEvent(int issueNumber = 42)
    {
        string branchName = $"feat/{issueNumber}-test-issue";
        ClaimedIssueDispatch dispatch = new(
            IssueId.New(),
            WorkerRunId.New(),
            issueNumber,
            "Test Issue",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            BranchName.From(branchName),
            MonitoredRepositoryId.New(),
            new WorkerProvider.GitHub(),
            new DispatchContext.Fresh(branchName),
            $"https://api.github.com/repos/owner/repo/issues/{issueNumber}");
        return new IssueClaimed(dispatch);
    }

    public async ValueTask DisposeAsync()
    {
        Starter.Dispose();
        await _services.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}
