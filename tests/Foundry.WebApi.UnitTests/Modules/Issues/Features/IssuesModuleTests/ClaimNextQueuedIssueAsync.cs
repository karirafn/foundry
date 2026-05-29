using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Features.IssuesModuleTests;

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
        _sut = new WorkerCapacityAvailableHandler(_dbContext, _dispatcher);
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
}
