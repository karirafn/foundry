using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.RetryIssueHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly StubIssueQueries _issueQueries;
    private readonly RetryIssue.Handler _sut;

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingDomainEventDispatcher();
        _issueQueries = new StubIssueQueries();
        _sut = new RetryIssue.Handler(_dbContext, _dispatcher, _issueQueries);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private FailedIssue SeedFailedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Issue 1",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        FailedIssue failed = inProgress.MarkFailed(Guid.NewGuid(), "Non-zero exit code: 1", DateTimeOffset.UtcNow);
        _dbContext.Set<Issue>().Add(failed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    private ContinuableFailedIssue SeedContinuableFailedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 2,
            title: "Issue 2",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "feat/2-issue-2",
            "Non-zero exit code: 1",
            DateTimeOffset.UtcNow);
        _dbContext.Set<Issue>().Add(failed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    private RevisionFailedIssue SeedRevisionFailedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 3,
            title: "Issue 3",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/3-issue-3",
            "https://github.com/owner/repo/pull/3",
            DateTimeOffset.UtcNow);
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please fix this.")];
        RevisionQueuedIssue revisionQueued = review.Revise(comments);
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(Guid.NewGuid());
        RevisionFailedIssue revisionFailed = revisionInProgress.MarkFailed(
            revisionInProgress.WorkerRunId,
            "Non-zero exit code: 2",
            DateTimeOffset.UtcNow);
        _dbContext.Set<Issue>().Add(revisionFailed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionFailed;
    }

    private QueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 4,
            title: "Issue 4",
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

    [Fact]
    public async Task WhenFailedIssue_TransitionsToQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = SeedFailedIssue(repositoryId);
        RetryIssue.Command command = new(failed.Id);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenFailedIssue_DispatchesIssueQueuedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = SeedFailedIssue(repositoryId);
        RetryIssue.Command command = new(failed.Id);

        // Act
        await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueQueued>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenContinuableFailedIssue_TransitionsToContinuationQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = SeedContinuableFailedIssue(repositoryId);
        RetryIssue.Command command = new(failed.Id);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<ContinuationQueuedIssue>();
    }

    [Fact]
    public async Task WhenContinuableFailedIssue_DispatchesIssueContinuationQueuedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = SeedContinuableFailedIssue(repositoryId);
        RetryIssue.Command command = new(failed.Id);

        // Act
        await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueContinuationQueued>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenRevisionFailedIssue_TransitionsToRevisionQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionFailedIssue failed = SeedRevisionFailedIssue(repositoryId);
        RetryIssue.Command command = new(failed.Id);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<RevisionQueuedIssue>();
    }

    [Fact]
    public async Task WhenRevisionFailedIssue_DispatchesIssueRevisionQueuedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionFailedIssue failed = SeedRevisionFailedIssue(repositoryId);
        RetryIssue.Command command = new(failed.Id);

        // Act
        await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueRevisionQueued>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenIssueInWrongState_ReturnsWrongStateError()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = SeedQueuedIssue(repositoryId);
        RetryIssue.Command command = new(queued.Id);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        Result<IssueDetail>.Failure failure = result.ShouldBeOfType<Result<IssueDetail>.Failure>();
        failure.Error.Code.ShouldBe(IssueErrors.WrongStateCode);
    }

    [Fact]
    public async Task WhenIssueInWrongState_NoStateChange()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = SeedQueuedIssue(repositoryId);
        RetryIssue.Command command = new(queued.Id);

        // Act
        await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<QueuedIssue>();
    }

    [Fact]
    public async Task WhenIssueNotFound_ReturnsNotFoundError()
    {
        // Arrange
        IssueId nonExistentId = IssueId.New();
        RetryIssue.Command command = new(nonExistentId);

        // Act
        Result<IssueDetail> result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        Result<IssueDetail>.Failure failure = result.ShouldBeOfType<Result<IssueDetail>.Failure>();
        failure.Error.Code.ShouldBe(IssueErrors.NotFoundCode);
    }

    private sealed class StubIssueQueries : IIssueQueries
    {
        private static readonly IssueDetail StubDetail = new(
            Id: Guid.Empty,
            IssueNumber: 1,
            Title: "stub",
            State: "queued",
            RepositorySlug: "owner/repo",
            ProviderType: "github",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/1",
            Author: "octocat",
            Labels: [],
            StateDetails: null);

        public Task<Result<IssueDetail>> GetIssueDetailAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueDetail>.Ok(StubDetail));

        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(new Dictionary<int, IssueSnapshot>());

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IssueSummary>>([]);

        public Task<IssueSummary?> GetIssueSummaryAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult<IssueSummary?>(null);

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);
    }
}
