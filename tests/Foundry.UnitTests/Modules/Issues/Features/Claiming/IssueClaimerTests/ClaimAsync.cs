using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.Claiming.IssueClaimerTests;

public sealed class ClaimAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEventDispatcher;
    private readonly CapturingIntegrationEventDispatcher _integrationEventDispatcher;

    private static readonly RepositoryDispatchInfo DefaultDispatchInfo = new(
        "owner/repo",
        new Uri("https://github.com/owner/repo.git"),
        "GITHUB_PAT",
        new WorkerProvider.GitHub(),
        "https://api.github.com/repos/owner/repo/issues");

    public ClaimAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _domainEventDispatcher = new CapturingDomainEventDispatcher();
        _integrationEventDispatcher = new CapturingIntegrationEventDispatcher();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IssueClaimer BuildClaimer(IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        return new IssueClaimer(
            _dbContext,
            integrationEventDispatcher ?? _integrationEventDispatcher,
            _domainEventDispatcher);
    }

    private FreshQueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber = 1, string title = "Add Health Check")
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle(title)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber = 20)
    {
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithBranchName($"feat/issue-{issueNumber}")
            .WithPullRequestUrl($"https://github.com/owner/repo/pull/{issueNumber}")
            .WithReviewComments([new ReviewComment("Please fix this.")])
            .RevisionQueued();
        _dbContext.Set<Issue>().Add(revisionQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionQueued;
    }

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(
        MonitoredRepositoryId repositoryId,
        string branchName = "feat/10-fix")
    {
        ContinuableFailedIssue continuableFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(10)
            .WithTitle("Issue 10")
            .WithBranchName(branchName)
            .WithFailureReason("Non-zero exit code: 1")
            .WithFailureCategory(FailureCategory.NonZeroExit)
            .ContinuableFailed();
        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
        _dbContext.Set<Issue>().Add(continuationQueued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return continuationQueued;
    }

    // Cycle 1: FreshQueuedIssue — issue transitions to InProgressIssue
    [Fact]
    public async Task WhenQueuedIssue_TransitionsToInProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId);
        DispatchCandidate candidate = new(queued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    // Cycle 2: FreshQueuedIssue — dispatches exactly one IssueClaimed event
    [Fact]
    public async Task WhenQueuedIssue_DispatchesExactlyOneIssueClaimedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId);
        DispatchCandidate candidate = new(queued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
    }

    // Cycle 3: FreshQueuedIssue — IssueClaimed payload has the correct branch name
    [Fact]
    public async Task WhenQueuedIssue_IssueClaimed_HasCorrectBranchName()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId, issueNumber: 42, title: "Add Health Check");
        DispatchCandidate candidate = new(queued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.BranchName.ShouldBe(BranchName.From("feat/42-add-health-check"));
    }

    // Cycle 4: FreshQueuedIssue — IssueClaimed payload has Fresh context
    [Fact]
    public async Task WhenQueuedIssue_IssueClaimed_HasFreshContext()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId, issueNumber: 42, title: "Add Health Check");
        DispatchCandidate candidate = new(queued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Context.ShouldBeOfType<DispatchContext.Fresh>();
    }

    // Cycle 5: FreshQueuedIssue — IssueClaimed payload has correct provider
    [Fact]
    public async Task WhenQueuedIssue_IssueClaimed_HasCorrectProvider()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId);
        RepositoryDispatchInfo gitLabInfo = new(
            "owner/repo",
            new Uri("https://gitlab.com/owner/repo.git"),
            "GITLAB_PAT",
            new WorkerProvider.GitLab(),
            "https://gitlab.com/api/v4/projects/owner%2Frepo/issues");
        DispatchCandidate candidate = new(queued, gitLabInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitLab>();
    }

    // Cycle 6: RevisionQueuedIssue — issue transitions to RevisionInProgressIssue
    [Fact]
    public async Task WhenRevisionQueuedIssue_TransitionsToRevisionInProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = SeedRevisionQueuedIssue(repositoryId);
        DispatchCandidate candidate = new(revisionQueued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<RevisionInProgressIssue>();
    }

    // Cycle 7: RevisionQueuedIssue — IssueClaimed payload has Revision context
    [Fact]
    public async Task WhenRevisionQueuedIssue_IssueClaimed_HasRevisionContext()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = SeedRevisionQueuedIssue(repositoryId, issueNumber: 20);
        DispatchCandidate candidate = new(revisionQueued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Context.ShouldBeOfType<DispatchContext.Revision>();
    }

    // Cycle 8: RevisionQueuedIssue — IssueClaimed payload has correct branch name from aggregate
    [Fact]
    public async Task WhenRevisionQueuedIssue_IssueClaimed_HasCorrectBranchName()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = SeedRevisionQueuedIssue(repositoryId, issueNumber: 20);
        DispatchCandidate candidate = new(revisionQueued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.BranchName.ShouldBe(BranchName.From("feat/issue-20"));
    }

    // Cycle 9: ContinuationQueuedIssue — issue transitions to InProgressIssue
    [Fact]
    public async Task WhenContinuationQueuedIssue_TransitionsToInProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = SeedContinuationQueuedIssue(repositoryId, branchName: "feat/10-fix");
        DispatchCandidate candidate = new(continuationQueued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    // Cycle 10: ContinuationQueuedIssue — IssueClaimed payload has Continuation context
    [Fact]
    public async Task WhenContinuationQueuedIssue_IssueClaimed_HasContinuationContext()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = SeedContinuationQueuedIssue(repositoryId, branchName: "feat/103-fix");
        DispatchCandidate candidate = new(continuationQueued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        DispatchContext.Continuation context = claimed.Dispatch.Context.ShouldBeOfType<DispatchContext.Continuation>();
        context.BranchName.ShouldBe("feat/103-fix");
    }

    // Cycle 11: ContinuationQueuedIssue — IssueClaimed payload has correct branch name from aggregate
    [Fact]
    public async Task WhenContinuationQueuedIssue_IssueClaimed_HasCorrectBranchName()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = SeedContinuationQueuedIssue(repositoryId, branchName: "feat/103-fix");
        DispatchCandidate candidate = new(continuationQueued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.BranchName.ShouldBe(BranchName.From("feat/103-fix"));
    }

    // Cycle 12: FreshQueuedIssue — IssueClaimed dispatch has IssueApiUrl built from base + issue number
    [Fact]
    public async Task WhenQueuedIssue_IssueClaimed_IssueApiUrlIsBaseJoinedWithIssueNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = SeedQueuedIssue(repositoryId, issueNumber: 42, title: "Add Health Check");
        DispatchCandidate candidate = new(queued, DefaultDispatchInfo);
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.IssueApiUrl.ShouldBe(DefaultDispatchInfo.IssueApiUrlBase + "/42");
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
