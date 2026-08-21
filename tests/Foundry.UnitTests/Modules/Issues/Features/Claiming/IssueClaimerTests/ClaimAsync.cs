using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
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

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ShouldBeOfType<Result<IssueAuthor>.Success>().Value;

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ShouldBeOfType<Result<ProviderUrl>.Success>().Value;

    private static readonly RepositoryDispatchInfo DefaultDispatchInfo = new(
        "owner/repo",
        new Uri("https://github.com/owner/repo.git"),
        "GITHUB_PAT",
        new WorkerProvider.GitHub());

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
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title: title,
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    private RevisionQueuedIssue SeedRevisionQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber = 20)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
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

    private ContinuationQueuedIssue SeedContinuationQueuedIssue(
        MonitoredRepositoryId repositoryId,
        string branchName = "feat/10-fix")
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
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            branchName,
            "Non-zero exit code: 1",
            "generic_failure",
            DateTimeOffset.UtcNow);
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
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
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
            new WorkerProvider.GitLab());
        DispatchCandidate candidate = new(queued, gitLabInfo);
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
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
        Guid workerRunId = Guid.NewGuid();
        IssueClaimer sut = BuildClaimer();

        // Act
        await sut.ClaimAsync(candidate, workerRunId, CancellationToken.None);

        // Assert
        IssueClaimed claimed = _integrationEventDispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.BranchName.ShouldBe(BranchName.From("feat/103-fix"));
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
