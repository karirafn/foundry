using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
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

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEventDispatcher;

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
        IRepositoryEligibilityQuery? repositoryEligibilityQuery = null,
        IRepositoryDispatchQueries? repositoryDispatchQueries = null,
        IIntegrationEventDispatcher? integrationEventDispatcher = null,
        IIssueBlockerQueries? issueBlockerQueries = null)
    {
        DispatchCandidateSelector selector = new(
            _dbContext,
            repositoryDispatchQueries ?? new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                new WorkerProvider.GitHub(),
                "https://api.github.com/repos/owner/repo/issues")),
            repositoryEligibilityQuery ?? new AllEligibleRepositoryEligibilityQuery(),
            new InFlightWorkerCountQuery(_dbContext),
            issueBlockerQueries ?? new StubIssueBlockerQueries(),
            IssueDependencyApplierFactory.Create(_dbContext, _domainEventDispatcher),
            NullLogger<DispatchCandidateSelector>.Instance);

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

    private FreshQueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber = 1)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    // Eligible repo: queued issue is claimed normally
    [Fact]
    public async Task WhenRepositoryIsEligible_TransitionsQueuedIssueToInProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories: [new EligibleRepository(repositoryId.Value, Position: 0, MaxConcurrentWorkers: 1)]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

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

    // Ineligible repo: queued issue is skipped (not claimed)
    [Fact]
    public async Task WhenRepositoryIsIneligible_DoesNotClaimQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleRepositories: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Unreachable repo (no eligibility record): queued issue is skipped
    [Fact]
    public async Task WhenRepositoryEligibilityIsUnknown_DoesNotClaimQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        // No eligible IDs — simulates repo with no eligibility record
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleRepositories: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Mixed: eligible and ineligible repos — only eligible one is claimed
    [Fact]
    public async Task WhenMixedEligibility_OnlyClaimsIssueFromEligibleRepository()
    {
        // Arrange
        MonitoredRepositoryId eligibleRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId ineligibleRepoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(eligibleRepoId, issueNumber: 1);
        SeedQueuedIssue(ineligibleRepoId, issueNumber: 2);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories: [new EligibleRepository(eligibleRepoId.Value, Position: 0, MaxConcurrentWorkers: 1)]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? eligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == eligibleRepoId,
                TestContext.Current.CancellationToken);
        Issue? ineligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == ineligibleRepoId,
                TestContext.Current.CancellationToken);
        eligibleIssue.ShouldBeOfType<InProgressIssue>();
        ineligibleIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Ineligible first in QueuedIssue tier: skip to next eligible candidate
    [Fact]
    public async Task WhenFirstQueuedIsIneligible_ClaimsNextQueuedFromEligibleRepository()
    {
        // Arrange
        MonitoredRepositoryId ineligibleRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId eligibleRepoId = MonitoredRepositoryId.New();

        // Ineligible issue has a lower issue number and earlier timestamp so it would otherwise head the queue
        FreshQueuedIssue ineligibleQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(ineligibleRepoId)
            .WithIssueNumber(1)
            .WithTitle("Ineligible Issue")
            .WithDetectedAt(DateTimeOffset.UtcNow.AddMinutes(-5))
            .FreshQueued();
        _dbContext.Set<Issue>().Add(ineligibleQueued);

        FreshQueuedIssue eligibleQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(eligibleRepoId)
            .WithIssueNumber(2)
            .WithTitle("Eligible Issue")
            .FreshQueued();
        _dbContext.Set<Issue>().Add(eligibleQueued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories: [new EligibleRepository(eligibleRepoId.Value, Position: 0, MaxConcurrentWorkers: 1)]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? ineligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == ineligibleRepoId,
                TestContext.Current.CancellationToken);
        Issue? eligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == eligibleRepoId,
                TestContext.Current.CancellationToken);
        ineligibleIssue.ShouldBeOfType<FreshQueuedIssue>();
        eligibleIssue.ShouldBeOfType<InProgressIssue>();
    }

    // Ineligible first in RevisionQueuedIssue tier: skip to next eligible candidate
    [Fact]
    public async Task WhenFirstRevisionQueuedIsIneligible_ClaimsNextRevisionQueuedFromEligibleRepository()
    {
        // Arrange
        MonitoredRepositoryId ineligibleRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId eligibleRepoId = MonitoredRepositoryId.New();

        // Ineligible revision issue has an earlier timestamp so it appears first
        SeedRevisionQueuedIssue(ineligibleRepoId, issueNumber: 20, detectedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        SeedRevisionQueuedIssue(eligibleRepoId, issueNumber: 21, detectedAt: DateTimeOffset.UtcNow);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories: [new EligibleRepository(eligibleRepoId.Value, Position: 0, MaxConcurrentWorkers: 1)]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? ineligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == ineligibleRepoId,
                TestContext.Current.CancellationToken);
        Issue? eligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == eligibleRepoId,
                TestContext.Current.CancellationToken);
        ineligibleIssue.ShouldBeOfType<RevisionQueuedIssue>();
        eligibleIssue.ShouldBeOfType<RevisionInProgressIssue>();
    }

    // Ineligible first in ContinuationQueuedIssue tier: skip to next eligible candidate
    [Fact]
    public async Task WhenFirstContinuationQueuedIsIneligible_ClaimsNextContinuationQueuedFromEligibleRepository()
    {
        // Arrange
        MonitoredRepositoryId ineligibleRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId eligibleRepoId = MonitoredRepositoryId.New();

        // Ineligible continuation issue has an earlier timestamp so it appears first
        SeedContinuationQueuedIssue(ineligibleRepoId, detectedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        SeedContinuationQueuedIssue(eligibleRepoId, detectedAt: DateTimeOffset.UtcNow);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories: [new EligibleRepository(eligibleRepoId.Value, Position: 0, MaxConcurrentWorkers: 1)]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? ineligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == ineligibleRepoId,
                TestContext.Current.CancellationToken);
        Issue? eligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == eligibleRepoId,
                TestContext.Current.CancellationToken);
        ineligibleIssue.ShouldBeOfType<ContinuationQueuedIssue>();
        eligibleIssue.ShouldBeOfType<InProgressIssue>();
    }

    // All revision candidates ineligible: fall through to continuation tier
    [Fact]
    public async Task WhenAllRevisionQueuedAreIneligible_FallsThroughToContinuationQueued()
    {
        // Arrange
        MonitoredRepositoryId ineligibleRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId continuationRepoId = MonitoredRepositoryId.New();

        SeedRevisionQueuedIssue(ineligibleRepoId);
        SeedContinuationQueuedIssue(continuationRepoId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories: [new EligibleRepository(continuationRepoId.Value, Position: 0, MaxConcurrentWorkers: 1)]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? revisionIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == ineligibleRepoId,
                TestContext.Current.CancellationToken);
        Issue? continuationIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == continuationRepoId,
                TestContext.Current.CancellationToken);
        revisionIssue.ShouldBeOfType<RevisionQueuedIssue>();
        continuationIssue.ShouldBeOfType<InProgressIssue>();
    }

    // All candidates across all tiers ineligible: no claim, no crash
    [Fact]
    public async Task WhenAllCandidatesAreIneligible_DoesNotClaimAnyIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(repositoryId);
        SeedContinuationQueuedIssue(repositoryId);
        SeedQueuedIssue(repositoryId, issueNumber: 99);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleRepositories: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act — should not throw
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<Issue> issues = await _dbContext.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .ToListAsync(TestContext.Current.CancellationToken);
        foreach (Issue issue in issues)
        {
            (issue is RevisionQueuedIssue or ContinuationQueuedIssue or FreshQueuedIssue).ShouldBeTrue();
        }
    }

    // Claim-priority ordering: revision queued takes precedence over continuation and fresh queued
    [Fact]
    public async Task WhenRevisionQueuedAndQueuedBothExist_PrioritizesRevisionQueued()
    {
        // Arrange
        MonitoredRepositoryId revisionRepositoryId = MonitoredRepositoryId.New();
        MonitoredRepositoryId queuedRepositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(revisionRepositoryId);
        SeedQueuedIssue(queuedRepositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery());

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? revisionIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == revisionRepositoryId,
                TestContext.Current.CancellationToken);
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepositoryId,
                TestContext.Current.CancellationToken);
        revisionIssue.ShouldBeOfType<RevisionInProgressIssue>();
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Claim-priority ordering: continuation queued takes precedence over fresh queued
    [Fact]
    public async Task WhenContinuationQueuedAndQueuedBothExist_PrioritizesContinuationQueued()
    {
        // Arrange
        MonitoredRepositoryId continuationRepositoryId = MonitoredRepositoryId.New();
        MonitoredRepositoryId queuedRepositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(continuationRepositoryId);
        SeedQueuedIssue(queuedRepositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery());

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

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
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Claim-priority ordering: revision queued takes precedence over continuation queued
    [Fact]
    public async Task WhenRevisionQueuedAndContinuationQueuedBothExist_PrioritizesRevisionQueued()
    {
        // Arrange
        MonitoredRepositoryId revisionRepositoryId = MonitoredRepositoryId.New();
        MonitoredRepositoryId continuationRepositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(revisionRepositoryId);
        SeedContinuationQueuedIssue(continuationRepositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery());

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

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

    // Ineligible revision queued: skipped (not claimed)
    [Fact]
    public async Task WhenRevisionQueuedRepositoryIsIneligible_SkipsRevisionQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleRepositories: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<RevisionQueuedIssue>();
    }

    // Ineligible continuation queued: skipped (not claimed)
    [Fact]
    public async Task WhenContinuationQueuedRepositoryIsIneligible_SkipsContinuationQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleRepositories: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<ContinuationQueuedIssue>();
    }

    [Fact]
    public async Task WhenQueuedIssueIsClaimed_DispatchProviderMatchesDiscriminator()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(10)
            .WithTitle("Issue 10")
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                new WorkerProvider.GitHub(),
                "https://api.github.com/repos/owner/repo/issues")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitHub>();
    }

    [Fact]
    public async Task WhenQueuedIssueIsClaimed_WithGitLabDiscriminator_DispatchProviderIsGitLab()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(11)
            .WithTitle("Issue 11")
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://gitlab.com/owner/repo.git"),
                "GITLAB_PAT",
                new WorkerProvider.GitLab(),
                "https://gitlab.com/api/v4/projects/owner%2Frepo/issues")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitLab>();
    }

    [Fact]
    public async Task WhenRevisionQueuedIssueIsClaimed_DispatchProviderMatchesDiscriminator()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(repositoryId);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                new WorkerProvider.GitHub(),
                "https://api.github.com/repos/owner/repo/issues")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitHub>();
    }

    [Fact]
    public async Task WhenContinuationQueuedIssueIsClaimed_DispatchProviderMatchesDiscriminator()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(repositoryId);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                new WorkerProvider.GitHub(),
                "https://api.github.com/repos/owner/repo/issues")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitHub>();
    }

    [Fact]
    public async Task WhenRevisionQueuedIssueIsClaimed_WithGitLabDiscriminator_DispatchProviderIsGitLab()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(repositoryId);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://gitlab.com/owner/repo.git"),
                "GITLAB_PAT",
                new WorkerProvider.GitLab(),
                "https://gitlab.com/api/v4/projects/owner%2Frepo/issues")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitLab>();
    }

    [Fact]
    public async Task WhenContinuationQueuedIssueIsClaimed_WithGitLabDiscriminator_DispatchProviderIsGitLab()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(repositoryId);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://gitlab.com/owner/repo.git"),
                "GITLAB_PAT",
                new WorkerProvider.GitLab(),
                "https://gitlab.com/api/v4/projects/owner%2Frepo/issues")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitLab>();
    }

    [Fact]
    public async Task WhenQueuedIssueIsClaimed_BranchNameIncludesTitleSlug()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(42)
            .WithTitle("Add Health Check")
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.BranchName.ShouldBe(BranchName.From("feat/42-add-health-check"));
    }

    [Fact]
    public async Task WhenContinuationQueuedIssueExists_ClaimsContinuationQueued()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedContinuationQueuedIssue(repositoryId, branchName: "feat/103-fix");

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

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
        claimed.Dispatch.Context.ShouldBeOfType<DispatchContext.Continuation>()
            .BranchName.ShouldBe("feat/103-fix");
    }

    // Repo priority (Position) within a tier — lower Position wins
    [Fact]
    public async Task WhenTwoQueuedIssuesInDifferentRepos_ClaimsIssueFromLowerPositionRepo()
    {
        // Arrange
        MonitoredRepositoryId highPriorityRepoId = MonitoredRepositoryId.New(); // position 0
        MonitoredRepositoryId lowPriorityRepoId = MonitoredRepositoryId.New();  // position 1

        // Position outranks issue number, so the lower-position repo wins.
        DateTimeOffset sameTime = DateTimeOffset.UtcNow;
        SeedQueuedIssueAtTime(highPriorityRepoId, issueNumber: 1, detectedAt: sameTime);
        SeedQueuedIssueAtTime(lowPriorityRepoId, issueNumber: 2, detectedAt: sameTime);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories:
                [
                    new EligibleRepository(highPriorityRepoId.Value, Position: 0, MaxConcurrentWorkers: 1),
                    new EligibleRepository(lowPriorityRepoId.Value, Position: 1, MaxConcurrentWorkers: 1),
                ]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? highPriorityIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == highPriorityRepoId,
                TestContext.Current.CancellationToken);
        Issue? lowPriorityIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == lowPriorityRepoId,
                TestContext.Current.CancellationToken);
        highPriorityIssue.ShouldBeOfType<InProgressIssue>();
        lowPriorityIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    // IssueNumber tiebreaker within the same repository and tier (ADR 0092)
    [Fact]
    public async Task WhenTwoFreshQueuedIssuesInSameRepo_ClaimsLowerIssueNumberEvenIfDetectedLater()
    {
        // Arrange — #12 is detected after #50; the lower issue number still wins
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        DateTimeOffset olderTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset newerTime = DateTimeOffset.UtcNow;
        SeedQueuedIssueAtTime(repoId, issueNumber: 50, detectedAt: olderTime);
        SeedQueuedIssueAtTime(repoId, issueNumber: 12, detectedAt: newerTime);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories:
                [
                    new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 1),
                ]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue issue12 = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.IssueNumber == 12, TestContext.Current.CancellationToken);
        Issue issue50 = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.IssueNumber == 50, TestContext.Current.CancellationToken);
        issue12.ShouldBeOfType<InProgressIssue>();
        issue50.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Tier still outranks IssueNumber within the same repository (ADR 0092, 0025)
    [Fact]
    public async Task WhenRevisionQueuedHasHigherIssueNumberInSameRepo_RevisionTierStillWins()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        DateTimeOffset sameTime = DateTimeOffset.UtcNow;
        SeedRevisionQueuedIssueAtTime(repoId, issueNumber: 50, detectedAt: sameTime);
        SeedQueuedIssueAtTime(repoId, issueNumber: 12, detectedAt: sameTime);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories:
                [
                    new EligibleRepository(repoId.Value, Position: 0, MaxConcurrentWorkers: 1),
                ]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue issue50 = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.IssueNumber == 50, TestContext.Current.CancellationToken);
        Issue issue12 = await _dbContext.Set<Issue>()
            .SingleAsync(i => i.IssueNumber == 12, TestContext.Current.CancellationToken);
        issue50.ShouldBeOfType<RevisionInProgressIssue>();
        issue12.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Cross-tier: high-position revision queued still beats low-position fresh queued
    [Fact]
    public async Task WhenRevisionQueuedHasHigherPositionThanQueuedIssue_RevisionTierStillWins()
    {
        // Arrange
        MonitoredRepositoryId freshRepoId = MonitoredRepositoryId.New();    // position 0 (highest priority)
        MonitoredRepositoryId revisionRepoId = MonitoredRepositoryId.New(); // position 1 (lower priority)

        SeedQueuedIssue(freshRepoId, issueNumber: 1);
        SeedRevisionQueuedIssue(revisionRepoId, issueNumber: 20);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories:
                [
                    new EligibleRepository(freshRepoId.Value, Position: 0, MaxConcurrentWorkers: 1),
                    new EligibleRepository(revisionRepoId.Value, Position: 1, MaxConcurrentWorkers: 1),
                ]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert — revision tier always beats fresh tier regardless of repo position
        _dbContext.ChangeTracker.Clear();
        Issue? freshIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == freshRepoId,
                TestContext.Current.CancellationToken);
        Issue? revisionIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == revisionRepoId,
                TestContext.Current.CancellationToken);
        freshIssue.ShouldBeOfType<FreshQueuedIssue>();
        revisionIssue.ShouldBeOfType<RevisionInProgressIssue>();
    }

    // Position ordering in revision tier
    [Fact]
    public async Task WhenTwoRevisionQueuedIssuesInDifferentRepos_ClaimsIssueFromLowerPositionRepo()
    {
        // Arrange
        MonitoredRepositoryId highPriorityRepoId = MonitoredRepositoryId.New(); // position 0
        MonitoredRepositoryId lowPriorityRepoId = MonitoredRepositoryId.New();  // position 1

        DateTimeOffset sameTime = DateTimeOffset.UtcNow;
        SeedRevisionQueuedIssueAtTime(highPriorityRepoId, issueNumber: 20, detectedAt: sameTime);
        SeedRevisionQueuedIssueAtTime(lowPriorityRepoId, issueNumber: 21, detectedAt: sameTime);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories:
                [
                    new EligibleRepository(highPriorityRepoId.Value, Position: 0, MaxConcurrentWorkers: 1),
                    new EligibleRepository(lowPriorityRepoId.Value, Position: 1, MaxConcurrentWorkers: 1),
                ]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? highPriorityIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == highPriorityRepoId,
                TestContext.Current.CancellationToken);
        Issue? lowPriorityIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == lowPriorityRepoId,
                TestContext.Current.CancellationToken);
        highPriorityIssue.ShouldBeOfType<RevisionInProgressIssue>();
        lowPriorityIssue.ShouldBeOfType<RevisionQueuedIssue>();
    }

    // Position ordering in continuation tier
    [Fact]
    public async Task WhenTwoContinuationQueuedIssuesInDifferentRepos_ClaimsIssueFromLowerPositionRepo()
    {
        // Arrange
        MonitoredRepositoryId highPriorityRepoId = MonitoredRepositoryId.New(); // position 0
        MonitoredRepositoryId lowPriorityRepoId = MonitoredRepositoryId.New();  // position 1

        DateTimeOffset sameTime = DateTimeOffset.UtcNow;
        SeedContinuationQueuedIssueAtTime(highPriorityRepoId, detectedAt: sameTime);
        SeedContinuationQueuedIssueAtTime(lowPriorityRepoId, detectedAt: sameTime);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories:
                [
                    new EligibleRepository(highPriorityRepoId.Value, Position: 0, MaxConcurrentWorkers: 1),
                    new EligibleRepository(lowPriorityRepoId.Value, Position: 1, MaxConcurrentWorkers: 1),
                ]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? highPriorityIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == highPriorityRepoId,
                TestContext.Current.CancellationToken);
        Issue? lowPriorityIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == lowPriorityRepoId,
                TestContext.Current.CancellationToken);
        highPriorityIssue.ShouldBeOfType<InProgressIssue>();
        lowPriorityIssue.ShouldBeOfType<ContinuationQueuedIssue>();
    }

    // AC8: IssueId tiebreak — when tier, Position, and IssueNumber are all identical
    // (possible only across repositories sharing a position), the lower IssueId is claimed.
    [Fact]
    public async Task WhenTwoQueuedIssuesHaveSameTierPositionAndIssueNumber_ClaimsIssueWithLowerIssueId()
    {
        // Arrange
        DateTimeOffset sameTime = DateTimeOffset.UtcNow;
        MonitoredRepositoryId repoAId = MonitoredRepositoryId.New();
        MonitoredRepositoryId repoBId = MonitoredRepositoryId.New();

        // Seed both issues with the same issue number; both repos share the same Position.
        FreshQueuedIssue issueA = SeedQueuedIssueAtTime(repoAId, issueNumber: 1, detectedAt: sameTime);
        FreshQueuedIssue issueB = SeedQueuedIssueAtTime(repoBId, issueNumber: 1, detectedAt: sameTime);

        // Determine which IssueId is lower by the same ordering used by DispatchOrderKey.
        bool issueAHasLowerId = issueA.Id.CompareTo(issueB.Id) < 0;
        MonitoredRepositoryId expectedWinnerRepoId = issueAHasLowerId ? repoAId : repoBId;
        MonitoredRepositoryId expectedLoserRepoId = issueAHasLowerId ? repoBId : repoAId;

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories:
                [
                    new EligibleRepository(repoAId.Value, Position: 0, MaxConcurrentWorkers: 1),
                    new EligibleRepository(repoBId.Value, Position: 0, MaxConcurrentWorkers: 1),
                ]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert — the issue with the lower IssueId is claimed
        _dbContext.ChangeTracker.Clear();
        Issue? winnerIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == expectedWinnerRepoId,
                TestContext.Current.CancellationToken);
        Issue? loserIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == expectedLoserRepoId,
                TestContext.Current.CancellationToken);
        winnerIssue.ShouldBeOfType<InProgressIssue>();
        loserIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Guards the TryGetValue refactoring: a candidate whose repo id is absent from the
    // eligible dictionary must be excluded without throwing, even when eligible repos exist.
    [Fact]
    public async Task WhenCandidateRepoIdAbsentFromEligibleDictionary_ExcludesCandidateWithoutThrowing()
    {
        // Arrange
        MonitoredRepositoryId absentRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId eligibleRepoId = MonitoredRepositoryId.New();

        // Seed issues for both repos
        SeedQueuedIssue(absentRepoId, issueNumber: 1);
        SeedQueuedIssue(eligibleRepoId, issueNumber: 2);

        // Only eligibleRepoId is returned from the eligibility query;
        // absentRepoId has a queued issue but no position entry in the dictionary.
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleRepositories: [new EligibleRepository(eligibleRepoId.Value, Position: 0, MaxConcurrentWorkers: 1)]));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act — must not throw a KeyNotFoundException
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert — the eligible repo's issue is claimed; the absent repo's issue is skipped
        _dbContext.ChangeTracker.Clear();
        Issue? absentIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == absentRepoId,
                TestContext.Current.CancellationToken);
        Issue? eligibleIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == eligibleRepoId,
                TestContext.Current.CancellationToken);
        absentIssue.ShouldBeOfType<FreshQueuedIssue>();
        eligibleIssue.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenQueuedIssueClaimed_PersistsProvidedWorkerRunId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);
        WorkerRunId workerRunId = WorkerRunId.New();

        WorkerCapacityAvailableHandler sut = BuildHandler();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        // Assert
        InProgressIssue? inProgress = await _dbContext.Set<Issue>()
            .OfType<InProgressIssue>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        inProgress.ShouldNotBeNull();
        inProgress.WorkerRunId.ShouldBe(workerRunId);
    }

    [Fact]
    public async Task WhenNoQueuedIssuesExist_DoesNotDispatchIssueClaimed()
    {
        // Arrange
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        capturingDispatcher.DispatchedEvents.OfType<IssueClaimed>().ShouldBeEmpty();
    }

    // Guard: if an issue already carries the incoming WorkerRunId, no transition occurs
    // even when other queued issues are available for claiming.
    [Fact]
    public async Task WhenIssueAlreadyCarriesWorkerRunId_DoesNotTransitionAnyIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId workerRunId = WorkerRunId.New();
        InProgressIssue inProgress = SeedInProgressIssue(repositoryId, workerRunId);

        // Also seed a queued issue so the guard must actively stop the selection.
        MonitoredRepositoryId queuedRepoId = MonitoredRepositoryId.New();
        SeedQueuedIssue(queuedRepoId, issueNumber: 77);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery(),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert — the existing in-progress issue is untouched; no IssueClaimed published;
        // the queued issue remains queued.
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == inProgress.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
        capturingDispatcher.DispatchedEvents.OfType<IssueClaimed>().ShouldBeEmpty();
        Issue? queuedIssue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == queuedRepoId,
                TestContext.Current.CancellationToken);
        queuedIssue.ShouldBeOfType<FreshQueuedIssue>();
    }

    // Guard redelivery: replaying the same WorkerCapacityAvailable after a claim committed
    // yields exactly one issue holding that WorkerRunId.
    [Fact]
    public async Task WhenClaimAlreadyCommittedAndEventRedelivered_ExactlyOneIssueHoldsWorkerRunId()
    {
        // Arrange — seed the issue already in-progress with the run id (simulates crash after claim committed).
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId workerRunId = WorkerRunId.New();
        SeedInProgressIssue(repositoryId, workerRunId);

        // Also seed a second queued issue to prove the guard stops selection entirely.
        SeedQueuedIssue(MonitoredRepositoryId.New(), issueNumber: 99);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new AllEligibleRepositoryEligibilityQuery());

        WorkerCapacityAvailable @event = new(workerRunId);

        // Act — redelivery
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert — exactly one issue holds this WorkerRunId; no in-progress issue is left without a run.
        _dbContext.ChangeTracker.Clear();
        List<Issue> allIssues = await _dbContext.Set<Issue>()
            .ToListAsync(TestContext.Current.CancellationToken);
        List<InProgressIssue> inProgressIssues = allIssues.OfType<InProgressIssue>().ToList();
        inProgressIssues.Count(i => i.WorkerRunId == workerRunId).ShouldBe(1);
    }

    private InProgressIssue SeedInProgressIssue(MonitoredRepositoryId repositoryId, WorkerRunId workerRunId)
    {
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(50)
            .WithTitle("In Progress Issue")
            .WithWorkerRunId(workerRunId)
            .InProgress();
        _dbContext.Set<Issue>().Add(inProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return inProgress;
    }

    private FreshQueuedIssue SeedQueuedIssueAtTime(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssueAtTime(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt)
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionQueued;
    }

    private ContinuationQueuedIssue SeedContinuationQueuedIssueAtTime(
        MonitoredRepositoryId repositoryId,
        DateTimeOffset detectedAt)
    {
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(10)
            .WithDetectedAt(detectedAt)
            .ContinuableFailed()
            .Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return continuationQueued;
    }

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(
        MonitoredRepositoryId repositoryId,
        string branchName = "feat/103-fix",
        DateTimeOffset? detectedAt = null)
    {
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(10)
            .WithDetectedAt(detectedAt ?? DateTimeOffset.UtcNow)
            .WithBranchName(branchName)
            .ContinuableFailed()
            .Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return continuationQueued;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssue(
        MonitoredRepositoryId repositoryId,
        int issueNumber = 20,
        DateTimeOffset? detectedAt = null)
    {
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithDetectedAt(detectedAt ?? DateTimeOffset.UtcNow)
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionQueued;
    }

    /// <summary>
    /// Stub that returns exactly the provided eligible repositories (with positions).
    /// </summary>
    // Claim-time blocker re-check (#731)
    [Fact]
    public async Task WhenHeadCandidateGainedBlocker_BlocksItAndClaimsNextCandidateInSameEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue head = SeedQueuedIssue(repositoryId, issueNumber: 1);
        FreshQueuedIssue next = SeedQueuedIssue(repositoryId, issueNumber: 2);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher,
            issueBlockerQueries: new StubIssueBlockerQueries().WithBlockers(1, 40));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        capturingDispatcher.DispatchedEvents.OfType<IssueClaimed>().ShouldHaveSingleItem()
            .Dispatch.IssueId.ShouldBe(next.Id);
        capturingDispatcher.DispatchedEvents.OfType<ClaimSkipped>().ShouldBeEmpty();

        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<Issue>().SingleAsync(i => i.Id == head.Id, TestContext.Current.CancellationToken))
            .ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([40]);
        (await _dbContext.Set<Issue>().SingleAsync(i => i.Id == next.Id, TestContext.Current.CancellationToken))
            .ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenEveryCandidateBlockedAtClaimTime_DispatchesClaimSkipped()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 1);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher,
            issueBlockerQueries: new StubIssueBlockerQueries().WithBlockers(1, 40));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        capturingDispatcher.DispatchedEvents.OfType<ClaimSkipped>().ShouldHaveSingleItem()
            .WorkerRunId.ShouldBe(@event.WorkerRunId);
        capturingDispatcher.DispatchedEvents.OfType<IssueClaimed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenEveryBlockerReadFails_DispatchesClaimSkippedAndLeavesCandidateQueued()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId, issueNumber: 1);

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher,
            issueBlockerQueries: new StubIssueBlockerQueries().WithFailure(1));

        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        capturingDispatcher.DispatchedEvents.OfType<ClaimSkipped>().ShouldHaveSingleItem();
        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<Issue>().SingleAsync(i => i.Id == queued.Id, TestContext.Current.CancellationToken))
            .ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenRedeliveredAfterBlockingThenThrowing_DoesNotReselectBlockedIssue()
    {
        // Arrange — first delivery blocks issue 1, then the claim of issue 2 throws
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue head = SeedQueuedIssue(repositoryId, issueNumber: 1);
        FreshQueuedIssue next = SeedQueuedIssue(repositoryId, issueNumber: 2);
        WorkerCapacityAvailable @event = new(WorkerRunId: WorkerRunId.New());

        WorkerCapacityAvailableHandler firstDelivery = BuildHandler(
            integrationEventDispatcher: new ThrowingIntegrationEventDispatcher(),
            issueBlockerQueries: new StubIssueBlockerQueries().WithBlockers(1, 40));

        await Should.ThrowAsync<InvalidOperationException>(
            () => firstDelivery.HandleAsync(@event, TestContext.Current.CancellationToken));

        // The relay clears the change tracker after a handler throws.
        _dbContext.ChangeTracker.Clear();

        StubIssueBlockerQueries redeliveryBlockerQueries = new StubIssueBlockerQueries().WithBlockers(1, 40);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler redelivery = BuildHandler(
            integrationEventDispatcher: capturingDispatcher,
            issueBlockerQueries: redeliveryBlockerQueries);

        // Act
        await redelivery.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        redeliveryBlockerQueries.RequestedIssueNumbers.ShouldBe([2]);
        capturingDispatcher.DispatchedEvents.OfType<IssueClaimed>().ShouldHaveSingleItem()
            .Dispatch.IssueId.ShouldBe(next.Id);
        _domainEventDispatcher.DispatchedEvents.OfType<IssueBlocked>().ShouldHaveSingleItem();

        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<Issue>().SingleAsync(i => i.Id == head.Id, TestContext.Current.CancellationToken))
            .ShouldBeOfType<BlockedIssue>();
    }

    [Fact]
    public async Task WhenCandidateIsRevisionQueued_ClaimsWithoutReadingBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revision = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(5)
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revision);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        StubIssueBlockerQueries blockerQueries = new StubIssueBlockerQueries().WithBlockers(5, 1);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher,
            issueBlockerQueries: blockerQueries);

        // Act
        await sut.HandleAsync(new WorkerCapacityAvailable(WorkerRunId.New()), TestContext.Current.CancellationToken);

        // Assert
        capturingDispatcher.DispatchedEvents.OfType<IssueClaimed>().ShouldHaveSingleItem()
            .Dispatch.IssueId.ShouldBe(revision.Id);
        blockerQueries.RequestedIssueNumbers.ShouldBeEmpty();
    }

    private sealed class ThrowingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated handler failure after a candidate was blocked.");
    }

    private sealed class StubRepositoryEligibilityQuery(IReadOnlyCollection<EligibleRepository> eligibleRepositories)
        : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<EligibleRepository> eligible = eligibleRepositories
                .Where(r => repositoryIds.Contains(r.Id))
                .ToList();
            return Task.FromResult(eligible);
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    /// <summary>
    /// Stub that marks every queried repository as eligible with default position 0.
    /// </summary>
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
                .Select(id => new EligibleRepository(id, Position: 0, MaxConcurrentWorkers: 1))
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
