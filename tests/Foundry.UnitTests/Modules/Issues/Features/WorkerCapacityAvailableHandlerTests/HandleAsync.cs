using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
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

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(
        MonitoredRepositoryId repositoryId,
        string branchName = "feat/103-fix",
        string latestProgress = "Step 1 complete")
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 10,
            title: "Issue 10",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            branchName,
            latestProgress,
            "Non-zero exit code: 1",
            DateTimeOffset.UtcNow);
        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return continuationQueued;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 20,
            title: "Issue 20",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/issue-20",
            "https://github.com/owner/repo/pull/20",
            DateTimeOffset.UtcNow);
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please fix this.")];
        RevisionQueuedIssue revisionQueued = review.Revise(comments);
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionQueued;
    }

    [Fact]
    public async Task WhenContinuationQueuedIssueExists_ClaimsContinuationQueued()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(repositoryId, branchName: "feat/103-fix", latestProgress: "Step 1 complete");

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher);

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

        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Continuation.ShouldNotBeNull()
            .ShouldSatisfyAllConditions(
                c => c.BranchName.ShouldBe("feat/103-fix"),
                c => c.LatestProgress.ShouldBe("Step 1 complete"));
    }

    [Fact]
    public async Task WhenBothContinuationQueuedAndQueuedIssueExist_ClaimsContinuationQueuedFirst()
    {
        // Arrange
        MonitoredRepositoryId continuationRepositoryId = MonitoredRepositoryId.New();
        MonitoredRepositoryId queuedRepositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(continuationRepositoryId);
        SeedQueuedIssue(queuedRepositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? continuationIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == continuationRepositoryId,
                TestContext.Current.CancellationToken);
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepositoryId,
                TestContext.Current.CancellationToken);
        continuationIssue.ShouldBeOfType<InProgressIssue>();
        queuedIssue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenRevisionQueuedAndContinuationQueuedBothExist_PrioritizesRevisionQueued()
    {
        // Arrange
        MonitoredRepositoryId revisionRepositoryId = MonitoredRepositoryId.New();
        MonitoredRepositoryId continuationRepositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(revisionRepositoryId);
        SeedContinuationQueuedIssue(continuationRepositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? revisionIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == revisionRepositoryId,
                TestContext.Current.CancellationToken);
        Issue? continuationIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == continuationRepositoryId,
                TestContext.Current.CancellationToken);
        revisionIssue.ShouldBeOfType<RevisionInProgressIssue>();
        continuationIssue.ShouldBeOfType<ContinuationQueuedIssue>();
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
