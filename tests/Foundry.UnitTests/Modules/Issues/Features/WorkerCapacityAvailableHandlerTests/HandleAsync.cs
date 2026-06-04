using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerCapacityAvailableHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEventDispatcher;

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    public HandleAsync()
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
        IBranchProtectionValidator? branchProtectionValidator = null,
        IRepositoryDispatchQueries? repositoryDispatchQueries = null,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        return new WorkerCapacityAvailableHandler(
            _dbContext,
            repositoryDispatchQueries ?? new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT")),
            integrationEventDispatcher ?? new NullIntegrationEventDispatcher(),
            branchProtectionValidator ?? new StubBranchProtectionValidator(violations: []),
            _domainEventDispatcher,
            NullLogger<WorkerCapacityAvailableHandler>.Instance);
    }

    private QueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber = 1)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    // Sub-task (d): validation passes → claiming proceeds as before
    [Fact]
    public async Task WhenBranchProtectionPasses_TransitionsQueuedIssueToInProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            branchProtectionValidator: new StubBranchProtectionValidator(violations: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    // Sub-task (b): validation returns violations → QueuedIssue → IneligibleIssue
    [Fact]
    public async Task WhenBranchProtectionReturnsViolations_TransitionsQueuedIssueToIneligible()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        IReadOnlyList<EligibilityViolationInfo> violations =
        [
            new EligibilityViolationInfo(
                "branch-protection:allow-direct-pushes",
                "Direct pushes allowed.")
        ];
        WorkerCapacityAvailableHandler sut = BuildHandler(
            branchProtectionValidator: new StubBranchProtectionValidator(violations: violations));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<IneligibleIssue>();
    }

    [Fact]
    public async Task WhenBranchProtectionReturnsViolations_DispatchesIssueIneligibleEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        IReadOnlyList<EligibilityViolationInfo> violations =
        [
            new EligibilityViolationInfo(
                "branch-protection:allow-direct-pushes",
                "Direct pushes allowed.")
        ];
        WorkerCapacityAvailableHandler sut = BuildHandler(
            branchProtectionValidator: new StubBranchProtectionValidator(violations: violations));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _domainEventDispatcher.DispatchedEvents
            .OfType<IssueIneligible>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenBranchProtectionReturnsViolations_PersistsViolations()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        IReadOnlyList<EligibilityViolationInfo> violations =
        [
            new EligibilityViolationInfo(
                "branch-protection:allow-direct-pushes",
                "Direct pushes allowed.")
        ];
        WorkerCapacityAvailableHandler sut = BuildHandler(
            branchProtectionValidator: new StubBranchProtectionValidator(violations: violations));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        IneligibleIssue ineligible = _dbContext.Set<Issue>()
            .OfType<IneligibleIssue>()
            .ShouldHaveSingleItem();
        ineligible.Violations.ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                v => v.Rule.ShouldBe("branch-protection:allow-direct-pushes"),
                v => v.Description.ShouldBe("Direct pushes allowed."));
    }

    // Sub-task (c): validation fails (unreachable) → IneligibleIssue
    [Fact]
    public async Task WhenBranchProtectionIsUnreachable_TransitionsQueuedIssueToIneligible()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            branchProtectionValidator: new FailingBranchProtectionValidator());

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<IneligibleIssue>();
    }

    [Fact]
    public async Task WhenBranchProtectionIsUnreachable_DispatchesIssueIneligibleEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            branchProtectionValidator: new FailingBranchProtectionValidator());

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _domainEventDispatcher.DispatchedEvents
            .OfType<IssueIneligible>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenBranchProtectionIsUnreachable_PersistsUnreachableViolation()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            branchProtectionValidator: new FailingBranchProtectionValidator());

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        IneligibleIssue ineligible = _dbContext.Set<Issue>()
            .OfType<IneligibleIssue>()
            .ShouldHaveSingleItem();
        ineligible.Violations.ShouldHaveSingleItem()
            .Rule.ShouldBe("branch-protection:unreachable");
    }

    private sealed class StubBranchProtectionValidator(
        IReadOnlyList<EligibilityViolationInfo> violations) : IBranchProtectionValidator
    {
        public Task<Result<IReadOnlyList<EligibilityViolationInfo>>> ValidateAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IReadOnlyList<EligibilityViolationInfo>>.Ok(violations));
    }

    private sealed class FailingBranchProtectionValidator : IBranchProtectionValidator
    {
        public Task<Result<IReadOnlyList<EligibilityViolationInfo>>> ValidateAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<IReadOnlyList<EligibilityViolationInfo>>.Fail(
                    new Error("BranchProtection.Unreachable", "Branch protection check failed")));
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

    private sealed class CapturingDomainEventDispatcher : IDomainEventDispatcher
    {
        private readonly List<IDomainEvent> _events = [];

        public IReadOnlyList<IDomainEvent> DispatchedEvents => _events;

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        {
            _events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
