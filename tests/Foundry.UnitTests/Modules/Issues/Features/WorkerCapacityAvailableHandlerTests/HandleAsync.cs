using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
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
        IRepositoryEligibilityQuery? repositoryEligibilityQuery = null,
        IRepositoryDispatchQueries? repositoryDispatchQueries = null,
        IIntegrationEventDispatcher? integrationEventDispatcher = null,
        IAuthValidator? authValidator = null,
        ISystemNotificationBroadcaster? systemNotificationBroadcaster = null)
    {
        return new WorkerCapacityAvailableHandler(
            _dbContext,
            repositoryDispatchQueries ?? new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                "github")),
            integrationEventDispatcher ?? new NullIntegrationEventDispatcher(),
            repositoryEligibilityQuery ?? new AllEligibleRepositoryEligibilityQuery(),
            _domainEventDispatcher,
            authValidator ?? new StubAuthValidator(AuthValidationResult.Valid()),
            systemNotificationBroadcaster ?? new NullSystemNotificationBroadcaster(),
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

    // Eligible repo: queued issue is claimed normally
    [Fact]
    public async Task WhenRepositoryIsEligible_TransitionsQueuedIssueToInProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleIds: [repositoryId.Value]));

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

    // Ineligible repo: queued issue is skipped (not claimed)
    [Fact]
    public async Task WhenRepositoryIsIneligible_DoesNotClaimQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleIds: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
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
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleIds: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
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
                eligibleIds: [eligibleRepoId.Value]));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
        ineligibleIssue.ShouldBeOfType<QueuedIssue>();
    }

    // Ineligible first in QueuedIssue tier: skip to next eligible candidate
    [Fact]
    public async Task WhenFirstQueuedIsIneligible_ClaimsNextQueuedFromEligibleRepository()
    {
        // Arrange
        MonitoredRepositoryId ineligibleRepoId = MonitoredRepositoryId.New();
        MonitoredRepositoryId eligibleRepoId = MonitoredRepositoryId.New();

        // Ineligible issue has an earlier timestamp so it appears first in OrderBy(DetectedAt)
        DetectedIssue ineligibleDetected = DetectedIssue.Detect(
            ineligibleRepoId,
            issueNumber: 1,
            title: "Ineligible Issue",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        QueuedIssue ineligibleQueued = QueuedIssue.FromDetected(ineligibleDetected);
        _dbContext.Set<Issue>().Add(ineligibleQueued);

        DetectedIssue eligibleDetected = DetectedIssue.Detect(
            eligibleRepoId,
            issueNumber: 2,
            title: "Eligible Issue",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue eligibleQueued = QueuedIssue.FromDetected(eligibleDetected);
        _dbContext.Set<Issue>().Add(eligibleQueued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(
                eligibleIds: [eligibleRepoId.Value]));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
        ineligibleIssue.ShouldBeOfType<QueuedIssue>();
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
                eligibleIds: [eligibleRepoId.Value]));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
                eligibleIds: [eligibleRepoId.Value]));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
                eligibleIds: [continuationRepoId.Value]));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleIds: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act — should not throw
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<Issue> issues = await _dbContext.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .ToListAsync(TestContext.Current.CancellationToken);
        foreach (Issue issue in issues)
        {
            (issue is RevisionQueuedIssue or ContinuationQueuedIssue or QueuedIssue).ShouldBeTrue();
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

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
        queuedIssue.ShouldBeOfType<QueuedIssue>();
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

    // Ineligible revision queued: skipped (not claimed)
    [Fact]
    public async Task WhenRevisionQueuedRepositoryIsIneligible_SkipsRevisionQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRevisionQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleIds: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
            repositoryEligibilityQuery: new StubRepositoryEligibilityQuery(eligibleIds: []));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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

    // Auth invalid: no issues claimed regardless of eligibility
    [Fact]
    public async Task WhenAuthIsInvalid_DoesNotClaimAnyIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId);

        WorkerCapacityAvailableHandler sut = BuildHandler(
            authValidator: new StubAuthValidator(AuthValidationResult.Invalid("API key is invalid")));

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenAuthIsInvalid_SendsActiveSystemNotificationWithErrorMessage()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            authValidator: new StubAuthValidator(AuthValidationResult.Invalid("API key is invalid")),
            systemNotificationBroadcaster: broadcaster);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.ShouldSatisfyAllConditions(
            () => notification.Category.ShouldBe("claude-auth"),
            () => notification.IsActive.ShouldBeTrue(),
            () => notification.Message.ShouldBe("API key is invalid"));
    }

    [Fact]
    public async Task WhenAuthIsValid_SendsClearSystemNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            authValidator: new StubAuthValidator(AuthValidationResult.Valid()),
            systemNotificationBroadcaster: broadcaster);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        SystemNotification notification = broadcaster.SentNotifications.ShouldHaveSingleItem();
        notification.ShouldSatisfyAllConditions(
            () => notification.Category.ShouldBe("claude-auth"),
            () => notification.IsActive.ShouldBeFalse(),
            () => notification.Message.ShouldBe(""));
    }

    [Fact]
    public async Task WhenQueuedIssueIsClaimed_DispatchProviderMatchesDiscriminator()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
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
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://github.com/owner/repo.git"),
                "GITHUB_PAT",
                "github")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 11,
            title: "Issue 11",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            repositoryDispatchQueries: new StubRepositoryDispatchQueries(new RepositoryDispatchInfo(
                "owner/repo",
                new Uri("https://gitlab.com/owner/repo.git"),
                "GITLAB_PAT",
                "gitlab")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
                "github")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

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
                "github")),
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitHub>();
    }

    [Fact]
    public async Task WhenQueuedIssueIsClaimed_BranchNameIncludesTitleSlug()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 42,
            title: "Add Health Check",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerCapacityAvailableHandler sut = BuildHandler(
            integrationEventDispatcher: capturingDispatcher);

        WorkerCapacityAvailable @event = new(WorkerRunId: Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = capturingDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.BranchName.ShouldBe("feat/42-add-health-check");
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
            .BranchName.ShouldBe("feat/103-fix");
    }

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(
        MonitoredRepositoryId repositoryId,
        string branchName = "feat/103-fix",
        DateTimeOffset? detectedAt = null)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 10,
            title: "Issue 10",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt ?? DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            branchName,
            "Non-zero exit code: 1",
            DateTimeOffset.UtcNow);
        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
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
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt ?? DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            $"feat/issue-{issueNumber}",
            $"https://github.com/owner/repo/pull/{issueNumber}",
            DateTimeOffset.UtcNow);
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please fix this.")];
        RevisionQueuedIssue revisionQueued = review.Revise(comments);
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionQueued;
    }

    /// <summary>
    /// Stub that returns exactly the provided eligible IDs.
    /// </summary>
    private sealed class StubRepositoryEligibilityQuery(IReadOnlyCollection<Guid> eligibleIds)
        : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlySet<Guid>> GetEligibleRepositoryIdsAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
        {
            HashSet<Guid> eligible = repositoryIds
                .Where(eligibleIds.Contains)
                .ToHashSet();
            return Task.FromResult<IReadOnlySet<Guid>>(eligible);
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    /// <summary>
    /// Stub that marks every queried repository as eligible.
    /// </summary>
    private sealed class AllEligibleRepositoryEligibilityQuery : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlySet<Guid>> GetEligibleRepositoryIdsAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<Guid>>(repositoryIds.ToHashSet());

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

    private sealed class StubAuthValidator(AuthValidationResult result) : IAuthValidator
    {
        public Task<AuthValidationResult> ValidateAsync(CancellationToken cancellationToken)
            => Task.FromResult(result);
    }

    private sealed class NullSystemNotificationBroadcaster : ISystemNotificationBroadcaster
    {
        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class CapturingSystemNotificationBroadcaster : ISystemNotificationBroadcaster
    {
        private readonly List<SystemNotification> _notifications = [];

        public IReadOnlyList<SystemNotification> SentNotifications => _notifications;

        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
        {
            _notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
