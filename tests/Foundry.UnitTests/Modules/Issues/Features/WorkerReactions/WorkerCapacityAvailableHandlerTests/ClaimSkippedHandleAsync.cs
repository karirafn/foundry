using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerCapacityAvailableHandlerTests;

/// <summary>
/// Verifies that WorkerCapacityAvailableHandler publishes ClaimSkipped on every non-Selected outcome.
/// </summary>
public sealed class ClaimSkippedHandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEventDispatcher;

    public ClaimSkippedHandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _domainEventDispatcher = new CapturingDomainEventDispatcher();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private WorkerCapacityAvailableHandler BuildHandler(
        IRepositoryEligibilityQuery? repositoryEligibilityQuery = null,
        IRepositoryDispatchQueries? repositoryDispatchQueries = null,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        DispatchCandidateSelector selector = new(
            _dbContext,
            repositoryDispatchQueries ?? new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                new WorkerProvider.GitHub(),
                "https://api.github.com/repos/owner/repo/issues")),
            repositoryEligibilityQuery ?? new AllEligibleRepositoryEligibilityQuery());

        IssueClaimer claimer = new(
            _dbContext,
            integrationEventDispatcher ?? new NullIntegrationEventDispatcher(),
            _domainEventDispatcher);

        return new WorkerCapacityAvailableHandler(
            _dbContext,
            selector,
            claimer,
            integrationEventDispatcher ?? new NullIntegrationEventDispatcher(),
            NullLogger<WorkerCapacityAvailableHandler>.Instance);
    }

    private async Task SeedQueuedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber = 1)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
    }

    // NoEligibleRepositories → ClaimSkipped published
    [Fact]
    public async Task WhenNoEligibleRepositories_PublishesClaimSkipped()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        await SeedQueuedIssueAsync(repositoryId);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new NoEligibleRepositoriesQuery(),
            integrationEventDispatcher: capturingDispatcher);

        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        ClaimSkipped skipped = capturingDispatcher.DispatchedEvents
            .OfType<ClaimSkipped>()
            .ShouldHaveSingleItem();
        skipped.WorkerRunId.ShouldBe(workerRunId);
    }

    // NoCandidates → ClaimSkipped published
    [Fact]
    public async Task WhenNoCandidates_PublishesClaimSkipped()
    {
        // Arrange — no queued issues at all → NoCandidates
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery(),
            integrationEventDispatcher: capturingDispatcher);

        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        ClaimSkipped skipped = capturingDispatcher.DispatchedEvents
            .OfType<ClaimSkipped>()
            .ShouldHaveSingleItem();
        skipped.WorkerRunId.ShouldBe(workerRunId);
    }

    // AllCandidatesUnresolvable → ClaimSkipped published
    [Fact]
    public async Task WhenAllCandidatesUnresolvable_PublishesClaimSkipped()
    {
        // Arrange — queued issue exists but dispatch info is null → AllCandidatesUnresolvable
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        await SeedQueuedIssueAsync(repositoryId);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery(),
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(null),
            integrationEventDispatcher: capturingDispatcher);

        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        ClaimSkipped skipped = capturingDispatcher.DispatchedEvents
            .OfType<ClaimSkipped>()
            .ShouldHaveSingleItem();
        skipped.WorkerRunId.ShouldBe(workerRunId);
    }

    // Selected → ClaimSkipped NOT published
    [Fact]
    public async Task WhenSelected_DoesNotPublishClaimSkipped()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        await SeedQueuedIssueAsync(repositoryId);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery(),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — Selected produces IssueClaimed but no ClaimSkipped
        capturingDispatcher.DispatchedEvents.OfType<ClaimSkipped>().ShouldBeEmpty();
        capturingDispatcher.DispatchedEvents.OfType<IssueClaimed>().ShouldHaveSingleItem();
    }

    // Guard path: already-claimed run id → ClaimSkipped NOT published (guard returns early)
    [Fact]
    public async Task WhenGuardFiresOnRedelivery_DoesNotPublishClaimSkipped()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId workerRunId = WorkerRunId.New();

        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(50)
            .WithWorkerRunId(workerRunId)
            .InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery(),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — guard returns early; nothing dispatched
        capturingDispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    private sealed class NoEligibleRepositoriesQuery : IRepositoryEligibilityQuery
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

    private sealed class StubRepositoryDispatchQueries(RepositoryDispatchInfo? info) : IRepositoryDispatchQueries
    {
        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(info);
    }

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _events = [];

        public IReadOnlyList<IIntegrationEvent> DispatchedEvents => _events;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
