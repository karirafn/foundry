using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class ClaimNextQueuedIssueAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher;
    private readonly WorkerCapacityAvailableHandler _sut;

    public ClaimNextQueuedIssueAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _dispatcher = new CapturingIntegrationEventDispatcher();
        RepositoryDispatchQueries repositoryDispatchQueries = new(_dbContext);
        _sut = new WorkerCapacityAvailableHandler(
            _dbContext,
            repositoryDispatchQueries,
            _dispatcher,
            new PassingBranchProtectionValidator(),
            NullLogger<WorkerCapacityAvailableHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private async Task<(MonitoredRepository, GitHubAccount)> SeedRepositoryAsync(
        string slug = "owner/repo",
        string secretKeyName = "GITHUB_TOKEN")
    {
        GitHubAccount account = GitHubAccount.Create(
            "Test Account",
            secretKeyName,
            new Uri("https://github.com"));

        RepositorySlug repositorySlug =
            ((Result<RepositorySlug>.Success)RepositorySlug.Create(slug)).Value;

        MonitoredRepository repository = MonitoredRepository.Create(
            repositorySlug,
            account.Id,
            pollInterval: null);

        _dbContext.Set<GitHubAccount>().Add(account);
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (repository, account);
    }

    private async Task<QueuedIssue> SeedQueuedIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber = 1,
        string title = "Test Issue",
        string body = "Test body")
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title,
            body,
            ValidAuthor,
            ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return queued;
    }

    private async Task<RevisionQueuedIssue> SeedRevisionQueuedIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber = 10,
        string branchName = "feat/issue-10-fix",
        string pullRequestUrl = "https://github.com/owner/repo/pull/10",
        IReadOnlyList<ReviewComment>? reviewComments = null)
    {
        IReadOnlyList<ReviewComment> comments = reviewComments ?? [];

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            "Revision issue",
            "Revision body",
            ValidAuthor,
            ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            branchName,
            pullRequestUrl,
            DateTimeOffset.UtcNow);
        RevisionQueuedIssue revisionQueued = review.Revise(comments);

        _dbContext.Set<Issue>().Add(revisionQueued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return revisionQueued;
    }

    [Fact]
    public async Task WhenNoQueuedIssuesExist_DoesNotDispatchIssueClaimed()
    {
        // Arrange (empty database)
        WorkerCapacityAvailable @event = new(Guid.NewGuid());

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.Captured.ShouldNotContain(e => e is IssueClaimed);
    }

    [Fact]
    public async Task WhenQueuedIssueExists_DispatchesIssueClaimedWithCorrectProperties()
    {
        // Arrange
        (MonitoredRepository repository, GitHubAccount account) =
            await SeedRepositoryAsync("myorg/myrepo", "MY_GITHUB_TOKEN");

        QueuedIssue queued = await SeedQueuedIssueAsync(
            repository.Id,
            issueNumber: 7,
            title: "Fix the bug",
            body: "Detailed description");

        Guid workerRunId = Guid.NewGuid();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = _dispatcher.Captured
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        ClaimedIssueDispatch dispatch = claimed.Dispatch;
        dispatch.ShouldSatisfyAllConditions(
            () => dispatch.IssueId.ShouldBe(queued.Id),
            () => dispatch.WorkerRunId.ShouldBe(workerRunId),
            () => dispatch.IssueNumber.ShouldBe(7),
            () => dispatch.Title.ShouldBe("Fix the bug"),
            () => dispatch.Body.ShouldBe("Detailed description"),
            () => dispatch.RepositorySlug.ShouldBe("myorg/myrepo"),
            () => dispatch.AccountSecretKeyName.ShouldBe("MY_GITHUB_TOKEN"),
            () => dispatch.CloneUrl.ToString().ShouldBe("https://github.com/myorg/myrepo.git"));
    }

    [Fact]
    public async Task WhenQueuedIssueExists_IssueIsTransitionedToInProgress()
    {
        // Arrange
        (MonitoredRepository repository, _) = await SeedRepositoryAsync();
        QueuedIssue queued = await SeedQueuedIssueAsync(repository.Id);

        WorkerCapacityAvailable @event = new(Guid.NewGuid());

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        Issue? issue = await _dbContext.Set<Issue>()
            .FindAsync([queued.Id], TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenBothRevisionQueuedAndQueuedIssueExist_ClaimsRevisionQueuedFirst()
    {
        // Arrange
        (MonitoredRepository repository, _) = await SeedRepositoryAsync();

        RevisionQueuedIssue revisionQueued =
            await SeedRevisionQueuedIssueAsync(repository.Id, issueNumber: 10);
        await SeedQueuedIssueAsync(repository.Id, issueNumber: 1);

        WorkerCapacityAvailable @event = new(Guid.NewGuid());

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert — only the RevisionQueuedIssue is transitioned; QueuedIssue stays queued
        Issue? claimed = await _dbContext.Set<Issue>()
            .FindAsync([revisionQueued.Id], TestContext.Current.CancellationToken);
        claimed.ShouldBeOfType<RevisionInProgressIssue>();

        Issue? stillQueued = await _dbContext.Set<Issue>()
            .OfType<QueuedIssue>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stillQueued.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenRevisionQueuedIssueExists_DispatchIncludesRevisionContext()
    {
        // Arrange
        (MonitoredRepository repository, _) = await SeedRepositoryAsync("myorg/myrepo", "MY_GITHUB_TOKEN");

        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Fix the null check", "src/Service.cs", Line: 42),
        ];

        RevisionQueuedIssue revisionQueued = await SeedRevisionQueuedIssueAsync(
            repository.Id,
            issueNumber: 10,
            branchName: "feat/issue-10-fix",
            pullRequestUrl: "https://github.com/owner/repo/pull/10",
            reviewComments: comments);

        Guid workerRunId = Guid.NewGuid();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = _dispatcher.Captured
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        ClaimedIssueDispatch dispatch = claimed.Dispatch;
        RevisionContext revision = dispatch.Revision.ShouldNotBeNull();
        revision.ShouldSatisfyAllConditions(
            () => revision.BranchName.ShouldBe("feat/issue-10-fix"),
            () => revision.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"),
            () => revision.Comments.Count.ShouldBe(1),
            () => revision.Comments[0].Body.ShouldBe("Fix the null check"),
            () => dispatch.IssueId.ShouldBe(revisionQueued.Id),
            () => dispatch.WorkerRunId.ShouldBe(workerRunId));
    }

    [Fact]
    public async Task WhenQueuedIssueExists_DispatchHasNullRevisionContext()
    {
        // Arrange
        (MonitoredRepository repository, _) = await SeedRepositoryAsync();
        await SeedQueuedIssueAsync(repository.Id);

        WorkerCapacityAvailable @event = new(Guid.NewGuid());

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = _dispatcher.Captured
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        claimed.Dispatch.Revision.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRevisionQueuedIssueExists_IssueTransitionsToRevisionInProgress()
    {
        // Arrange
        (MonitoredRepository repository, _) = await SeedRepositoryAsync();
        RevisionQueuedIssue revisionQueued = await SeedRevisionQueuedIssueAsync(repository.Id);

        WorkerCapacityAvailable @event = new(Guid.NewGuid());

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        Issue? issue = await _dbContext.Set<Issue>()
            .FindAsync([revisionQueued.Id], TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<RevisionInProgressIssue>();
    }

    [Fact]
    public async Task WhenQueuedIssueExists_InProgressIssueStoresProvidedWorkerRunId()
    {
        // Arrange
        (MonitoredRepository repository, _) = await SeedRepositoryAsync();
        await SeedQueuedIssueAsync(repository.Id);
        Guid workerRunId = Guid.NewGuid();

        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert — the persisted InProgressIssue.WorkerRunId matches the provided guid
        InProgressIssue? inProgress = await _dbContext.Set<Issue>()
            .OfType<InProgressIssue>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        inProgress.ShouldNotBeNull();
        inProgress.WorkerRunId.ShouldBe(workerRunId);
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
    }

    private sealed class PassingBranchProtectionValidator : IBranchProtectionValidator
    {
        public Task<Result<IReadOnlyList<EligibilityViolationInfo>>> ValidateAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<IReadOnlyList<EligibilityViolationInfo>>.Ok(
                    Array.Empty<EligibilityViolationInfo>()));
    }
}
