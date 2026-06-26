using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetActiveIssueSummariesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    private static readonly DateTimeOffset Now = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public GetActiveIssueSummariesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueQueries(_dbContext, new NullRepositorySlugQueries(), new NullRepositoryEligibilityQuery());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private DetectedIssue SeedDetectedIssue(MonitoredRepositoryId repositoryId, int issueNumber, DateTimeOffset? detectedAt = null)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt ?? Now);
        _dbContext.Set<Issue>().Add(detected);
        _dbContext.SaveChanges();
        return detected;
    }

    private QueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        return queued;
    }

    private CompletedIssue SeedCompletedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            Now);
        CompletedIssue completed = review.Complete(Now);
        _dbContext.Set<Issue>().Add(completed);
        _dbContext.SaveChanges();
        return completed;
    }

    private UnchangedIssue SeedUnchangedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: Now);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        UnchangedIssue unchanged = inProgress.MarkUnchanged(Guid.NewGuid());
        _dbContext.Set<Issue>().Add(unchanged);
        _dbContext.SaveChanges();
        return unchanged;
    }

    [Fact]
    public async Task WhenNullStates_ReturnsAllActiveIssues()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);
        SeedQueuedIssue(repositoryId, issueNumber: 2);
        SeedCompletedIssue(repositoryId, issueNumber: 3);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(s => s.State == "detected");
        result.ShouldContain(s => s.State == "queued");
        result.ShouldNotContain(s => s.State == "completed");
    }

    [Fact]
    public async Task WhenEmptyStates_ReturnsAllActiveIssues()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);
        SeedCompletedIssue(repositoryId, issueNumber: 2);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: [],
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].State.ShouldBe("detected");
    }

    [Fact]
    public async Task WhenExplicitStateSubset_ReturnsOnlyThoseStates()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);
        SeedQueuedIssue(repositoryId, issueNumber: 2);
        SeedCompletedIssue(repositoryId, issueNumber: 3);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: ["detected"],
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].State.ShouldBe("detected");
    }

    [Fact]
    public async Task WhenFilteredByRepositoryId_ReturnsOnlyMatchingRepoIssues()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();
        SeedDetectedIssue(targetRepo, issueNumber: 1);
        SeedDetectedIssue(otherRepo, issueNumber: 2);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetActiveIssueSummariesAsync(
            repositoryId: targetRepo,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenMultipleActiveIssues_OrderedByDetectedAtDescending()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset earlier = Now.AddHours(-2);
        DateTimeOffset later = Now.AddHours(-1);
        SeedDetectedIssue(repositoryId, issueNumber: 1, detectedAt: earlier);
        SeedDetectedIssue(repositoryId, issueNumber: 2, detectedAt: later);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result[0].IssueNumber.ShouldBe(2);
        result[1].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenResolvedStatesExist_ExcludesThemFromNullStatesResult()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedDetectedIssue(repositoryId, issueNumber: 1);
        SeedCompletedIssue(repositoryId, issueNumber: 2);
        SeedUnchangedIssue(repositoryId, issueNumber: 3);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetActiveIssueSummariesAsync(
            repositoryId: null,
            states: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].State.ShouldBe("detected");
    }
}
