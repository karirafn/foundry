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
/// Exhaustiveness guard for <c>WorkerCapacityAvailableHandler.WorkerRunIdExistsAsync</c>.
///
/// The guard enumerates a fixed set of concrete <see cref="Issue"/> subclasses that carry
/// <c>WorkerRunId</c>. If a new run-carrying state is added without being added to the guard,
/// a duplicate-claim can slip through. This test discovers the run-carrying set via reflection
/// and asserts it matches the guard's expected set exactly — a compile-time guarantee is
/// unavailable because EF Core's typed-set queries require the concrete type at authoring time.
///
/// See also: <c>WorkerCapacityAvailableHandler.WorkerRunIdExistsAsync</c> for the guard.
/// </summary>
public sealed class WorkerRunIdGuardExhaustivenessTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEventDispatcher;

    /// <summary>
    /// The exact set of concrete <see cref="Issue"/> subtypes whose guard is checked in
    /// <c>WorkerRunIdExistsAsync</c>. Must stay in sync with that method.
    /// </summary>
    private static readonly IReadOnlySet<Type> GuardedTypes = new HashSet<Type>
    {
        typeof(InProgressIssue),
        typeof(RevisionInProgressIssue),
        typeof(ReviewIssue),
        typeof(UnchangedIssue),
        typeof(FailedIssue),
        typeof(ContinuableFailedIssue),
        typeof(RevisionFailedIssue),
    };

    public WorkerRunIdGuardExhaustivenessTests()
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

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Discovers all concrete <see cref="Issue"/> subclasses in the module assembly that
    /// declare a public <c>WorkerRunId</c> property (the canonical marker for run-carrying
    /// states), and asserts the guard's set (<see cref="GuardedTypes"/>) is identical.
    /// If this test fails, update <c>WorkerRunIdExistsAsync</c> AND <see cref="GuardedTypes"/>
    /// to include the new state.
    /// </summary>
    [Fact]
    public void GuardedTypesMatchAllRunCarryingIssueSubclasses()
    {
        // Arrange — reflect over the module assembly for all concrete Issue subclasses.
        Type issueBase = typeof(Issue);
        Type workerRunIdType = typeof(WorkerRunId);

        IReadOnlySet<Type> runCarryingTypes = issueBase.Assembly
            .GetTypes()
            .Where(t => t.IsClass)
            .Where(t => !t.IsAbstract)
            .Where(t => issueBase.IsAssignableFrom(t))
            .Where(t => t.GetProperty(nameof(WorkerRunId), workerRunIdType) is not null)
            .ToHashSet();

        // Act / Assert — the guarded set must be identical to the reflected run-carrying set.
        // A missing type means WorkerRunIdExistsAsync can let a duplicate-claim slip through.
        // An extra type means the guard is checking a state that no longer carries WorkerRunId.
        runCarryingTypes.ShouldBe(
            GuardedTypes,
            ignoreOrder: true,
            customMessage:
                "WorkerRunIdExistsAsync does not cover all run-carrying Issue subclasses. " +
                "Add or remove the type from both WorkerRunIdExistsAsync and GuardedTypes.");
    }

    /// <summary>
    /// Verifies that when an <see cref="InProgressIssue"/> already carries the incoming
    /// <see cref="WorkerRunId"/>, the handler returns early and makes no new claim.
    /// </summary>
    [Fact]
    public async Task WhenInProgressIssueCarriesWorkerRunId_GuardFiresAndNothingClaimed()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();

        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(50)
            .WithWorkerRunId(workerRunId)
            .InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepositoryId queuedRepoId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(queuedRepoId)
            .WithIssueNumber(99)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — queued issue must remain queued; guard prevented any claim.
        _dbContext.ChangeTracker.Clear();
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepoId,
                TestContext.Current.CancellationToken);
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    /// <summary>
    /// Verifies that when a <see cref="RevisionInProgressIssue"/> already carries the
    /// incoming <see cref="WorkerRunId"/>, the guard fires and prevents a new claim.
    /// </summary>
    [Fact]
    public async Task WhenRevisionInProgressIssueCarriesWorkerRunId_GuardFiresAndNothingClaimed()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();

        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(60)
            .WithWorkerRunId(workerRunId)
            .RevisionInProgress();
        _dbContext.Set<Issue>().Add(revisionInProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepositoryId queuedRepoId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(queuedRepoId)
            .WithIssueNumber(99)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepoId,
                TestContext.Current.CancellationToken);
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    /// <summary>
    /// Verifies that when a <see cref="ReviewIssue"/> already carries the incoming
    /// <see cref="WorkerRunId"/>, the guard fires and prevents a new claim.
    /// </summary>
    [Fact]
    public async Task WhenReviewIssueCarriesWorkerRunId_GuardFiresAndNothingClaimed()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();

        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(70)
            .WithWorkerRunId(workerRunId)
            .Review();
        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepositoryId queuedRepoId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(queuedRepoId)
            .WithIssueNumber(99)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepoId,
                TestContext.Current.CancellationToken);
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    /// <summary>
    /// Verifies that when an <see cref="UnchangedIssue"/> already carries the incoming
    /// <see cref="WorkerRunId"/>, the guard fires and prevents a new claim.
    /// </summary>
    [Fact]
    public async Task WhenUnchangedIssueCarriesWorkerRunId_GuardFiresAndNothingClaimed()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();

        UnchangedIssue unchanged = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(71)
            .WithWorkerRunId(workerRunId)
            .Unchanged();
        _dbContext.Set<Issue>().Add(unchanged);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepositoryId queuedRepoId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(queuedRepoId)
            .WithIssueNumber(99)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepoId,
                TestContext.Current.CancellationToken);
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    /// <summary>
    /// Verifies that when a <see cref="FailedIssue"/> already carries the incoming
    /// <see cref="WorkerRunId"/>, the guard fires and prevents a new claim.
    /// </summary>
    [Fact]
    public async Task WhenFailedIssueCarriesWorkerRunId_GuardFiresAndNothingClaimed()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();

        FailedIssue failed = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(72)
            .WithWorkerRunId(workerRunId)
            .Failed();
        _dbContext.Set<Issue>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepositoryId queuedRepoId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(queuedRepoId)
            .WithIssueNumber(99)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepoId,
                TestContext.Current.CancellationToken);
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    /// <summary>
    /// Verifies that when a <see cref="ContinuableFailedIssue"/> already carries the incoming
    /// <see cref="WorkerRunId"/>, the guard fires and prevents a new claim.
    /// </summary>
    [Fact]
    public async Task WhenContinuableFailedIssueCarriesWorkerRunId_GuardFiresAndNothingClaimed()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();

        ContinuableFailedIssue continuableFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(73)
            .WithWorkerRunId(workerRunId)
            .ContinuableFailed();
        _dbContext.Set<Issue>().Add(continuableFailed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepositoryId queuedRepoId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(queuedRepoId)
            .WithIssueNumber(99)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepoId,
                TestContext.Current.CancellationToken);
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    /// <summary>
    /// Verifies that when a <see cref="RevisionFailedIssue"/> already carries the incoming
    /// <see cref="WorkerRunId"/>, the guard fires and prevents a new claim.
    /// </summary>
    [Fact]
    public async Task WhenRevisionFailedIssueCarriesWorkerRunId_GuardFiresAndNothingClaimed()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();

        RevisionFailedIssue revisionFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(74)
            .WithWorkerRunId(workerRunId)
            .RevisionFailed();
        _dbContext.Set<Issue>().Add(revisionFailed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepositoryId queuedRepoId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(queuedRepoId)
            .WithIssueNumber(99)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepoId,
                TestContext.Current.CancellationToken);
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    private WorkerCapacityAvailableHandler BuildHandler(
        IRepositoryEligibilityQuery? repositoryEligibilityQuery = null,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        DispatchCandidateSelector selector = new(
            _dbContext,
            new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
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
}
