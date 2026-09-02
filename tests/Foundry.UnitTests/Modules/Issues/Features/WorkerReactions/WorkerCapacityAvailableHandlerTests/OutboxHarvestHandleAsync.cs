using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerCapacityAvailableHandlerTests;

/// <summary>
/// Verifies that WorkerCapacityAvailableHandler enqueues IssueClaimed before TransitionAsync
/// so the outbox interceptor harvests the event atomically with the claim transition.
/// </summary>
public sealed class OutboxHarvestHandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public OutboxHarvestHandleAsync()
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

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });

        return services.BuildServiceProvider();
    }

    private static FreshQueuedIssue SeedQueuedIssue(FoundryDbContext dbContext, MonitoredRepositoryId repositoryId)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .FreshQueued();
        dbContext.Set<Issue>().Add(queued);
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
        return queued;
    }

    [Fact]
    public async Task WhenQueuedIssueClaimed_IssuedClaimedRowPersistedAtomicallyWithTransition()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(dbContext, repositoryId);

        DispatchCandidateSelector selector = new(
            dbContext,
            new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                new WorkerProvider.GitHub())),
            new AllEligibleRepositoryEligibilityQuery());
        IssueClaimer claimer = new(dbContext, integrationEventDispatcher, new CapturingDomainEventDispatcher());
        WorkerCapacityAvailableHandler sut = new(
            selector,
            claimer,
            NullLogger<WorkerCapacityAvailableHandler>.Instance);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — issue transitioned to InProgress
        dbContext.ChangeTracker.Clear();
        Issue? issue = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();

        // Assert — IssueClaimed outbox row written atomically with transition
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(IssueClaimed)));
    }

    [Fact]
    public async Task WhenRevisionQueuedIssueClaimed_IssuedClaimedRowPersistedAtomicallyWithTransition()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(dbContext, repositoryId);

        DispatchCandidateSelector selector = new(
            dbContext,
            new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                new WorkerProvider.GitHub())),
            new AllEligibleRepositoryEligibilityQuery());
        IssueClaimer claimer = new(dbContext, integrationEventDispatcher, new CapturingDomainEventDispatcher());
        WorkerCapacityAvailableHandler sut = new(
            selector,
            claimer,
            NullLogger<WorkerCapacityAvailableHandler>.Instance);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — issue transitioned to RevisionInProgress
        dbContext.ChangeTracker.Clear();
        Issue? issue = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<RevisionInProgressIssue>();

        // Assert — IssueClaimed outbox row written atomically with revision transition
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(IssueClaimed)));
    }

    [Fact]
    public async Task WhenContinuationQueuedIssueClaimed_IssuedClaimedRowPersistedAtomicallyWithTransition()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(dbContext, repositoryId);

        DispatchCandidateSelector selector = new(
            dbContext,
            new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                new WorkerProvider.GitHub())),
            new AllEligibleRepositoryEligibilityQuery());
        IssueClaimer claimer = new(dbContext, integrationEventDispatcher, new CapturingDomainEventDispatcher());
        WorkerCapacityAvailableHandler sut = new(
            selector,
            claimer,
            NullLogger<WorkerCapacityAvailableHandler>.Instance);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — continuation issue transitioned to InProgress
        dbContext.ChangeTracker.Clear();
        Issue? issue = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();

        // Assert — IssueClaimed outbox row written atomically with continuation transition
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(IssueClaimed)));
    }

    [Fact]
    public async Task WhenNoEligibleRepository_NoOutboxRowWritten()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(dbContext, repositoryId);

        DispatchCandidateSelector selector = new(
            dbContext,
            new StubRepositoryDispatchQueries(null),
            new NoEligibleRepositoryEligibilityQuery());
        IssueClaimer claimer = new(dbContext, integrationEventDispatcher, new CapturingDomainEventDispatcher());
        WorkerCapacityAvailableHandler sut = new(
            selector,
            claimer,
            NullLogger<WorkerCapacityAvailableHandler>.Instance);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — no claim, no outbox row
        dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }

    private static void SeedRevisionQueuedIssue(FoundryDbContext dbContext, MonitoredRepositoryId repositoryId)
    {
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(20)
            .RevisionQueued();
        dbContext.Set<Issue>().Add(revisionQueued);
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
    }

    private static void SeedContinuationQueuedIssue(FoundryDbContext dbContext, MonitoredRepositoryId repositoryId)
    {
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(10)
            .ContinuableFailed()
            .Retry();
        dbContext.Set<Issue>().Add(continuationQueued);
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
    }

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

    private sealed class NoEligibleRepositoryEligibilityQuery : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<EligibleRepository>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class StubRepositoryDispatchQueries(RepositoryDispatchInfo? info) : IRepositoryDispatchQueries
    {
        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(info);
    }
}
