using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueSummariesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;
    private readonly StubRepositorySlugQueries _slugQueries;

    public GetIssueSummariesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _slugQueries = new StubRepositorySlugQueries();
        _sut = new IssueQueries(_dbContext, _slugQueries, new NullRepositoryEligibilityQuery(), new NullWorkerRunQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private async Task<DetectedIssue> CreateAndPersistDetectedIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        DateTimeOffset detectedAt)
    {
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: detectedAt);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return issue;
    }

    [Fact]
    public async Task WhenNoIssuesExist_ReturnsEmptyList()
    {
        // Arrange & Act
        IReadOnlyList<IssueSummary> result = await _sut.GetIssueSummariesAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssuesExist_ReturnsSummaryWithSlug()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        const string slug = "owner/repo";
        _slugQueries.AddSlug(repositoryId, slug);

        DateTimeOffset detectedAt = DateTimeOffset.UtcNow;
        DetectedIssue issue = await CreateAndPersistDetectedIssueAsync(repositoryId, issueNumber: 1, detectedAt);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetIssueSummariesAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldHaveSingleItem();
        summary.ShouldSatisfyAllConditions(
            () => summary.IssueNumber.ShouldBe(1),
            () => summary.Title.ShouldBe("Issue 1"),
            () => summary.RepositorySlug.ShouldBe(slug),
            () => summary.State.ShouldBe("detected"),
            () => summary.Id.ShouldBe(issue.Id.Value),
            () => summary.DetectedAt.ShouldBe(detectedAt, tolerance: TimeSpan.FromSeconds(1)),
            () => summary.Url.ShouldBe(ValidUrl.Value.ToString()));
    }

    [Fact]
    public async Task WhenFilteredByRepositoryId_ReturnsOnlyMatchingIssues()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();
        _slugQueries.AddSlug(targetRepo, "owner/target");
        _slugQueries.AddSlug(otherRepo, "owner/other");

        await CreateAndPersistDetectedIssueAsync(targetRepo, issueNumber: 1, DateTimeOffset.UtcNow);
        await CreateAndPersistDetectedIssueAsync(otherRepo, issueNumber: 2, DateTimeOffset.UtcNow);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetIssueSummariesAsync(
            repositoryId: targetRepo,
            TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldHaveSingleItem();
        summary.IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenMultipleIssuesExist_SortedByDetectedAtDescending()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        _slugQueries.AddSlug(repositoryId, "owner/repo");

        DateTimeOffset earlier = DateTimeOffset.UtcNow.AddHours(-2);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddHours(-1);

        await CreateAndPersistDetectedIssueAsync(repositoryId, issueNumber: 1, detectedAt: earlier);
        await CreateAndPersistDetectedIssueAsync(repositoryId, issueNumber: 2, detectedAt: later);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetIssueSummariesAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result[0].IssueNumber.ShouldBe(2);
        result[1].IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenDetectedIssueExists_FailureClassificationIsNull()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        _slugQueries.AddSlug(repositoryId, "owner/repo");
        await CreateAndPersistDetectedIssueAsync(repositoryId, issueNumber: 1, DateTimeOffset.UtcNow);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetIssueSummariesAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldHaveSingleItem();
        summary.FailureClassification.ShouldBeNull();
    }

    [Fact]
    public async Task WhenUsageLimitedFailedIssueExists_FailureClassificationIsUsageLimited()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        _slugQueries.AddSlug(repositoryId, "owner/repo");

        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(repositoryId, issueNumber: 1, DateTimeOffset.UtcNow);
        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        FailedIssue failed = inProgress.MarkFailed(Guid.NewGuid(), "Usage limit reached", DateTimeOffset.UtcNow, "usage_limited");
        await _dbContext.TransitionAsync(inProgress, failed, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetIssueSummariesAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldHaveSingleItem();
        summary.FailureClassification.ShouldBe("usage_limited");
    }

    [Fact]
    public async Task WhenUsageLimitedContinuableFailedIssueExists_FailureClassificationIsUsageLimited()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        _slugQueries.AddSlug(repositoryId, "owner/repo");

        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(repositoryId, issueNumber: 1, DateTimeOffset.UtcNow);
        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
            workerRunId,
            branchName: "feat/issue-1",
            failureReason: "Usage limit reached",
            failureCategory: "usage_limited",
            failedAt: DateTimeOffset.UtcNow);
        await _dbContext.TransitionAsync(inProgress, continuableFailed, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetIssueSummariesAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldHaveSingleItem();
        summary.FailureClassification.ShouldBe("usage_limited");
    }

    [Fact]
    public async Task WhenFailedIssueExists_FailureClassificationEqualsStoredCategory()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        _slugQueries.AddSlug(repositoryId, "owner/repo");

        DetectedIssue detected = await CreateAndPersistDetectedIssueAsync(repositoryId, issueNumber: 1, DateTimeOffset.UtcNow);
        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        FailedIssue failed = inProgress.MarkFailed(Guid.NewGuid(), "Non-zero exit code: 1", DateTimeOffset.UtcNow, "generic_failure");
        await _dbContext.TransitionAsync(inProgress, failed, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<IssueSummary> result = await _sut.GetIssueSummariesAsync(
            repositoryId: null,
            TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = result.ShouldHaveSingleItem();
        summary.FailureClassification.ShouldBe("generic_failure");
    }

    private sealed class StubRepositorySlugQueries : IRepositorySlugQueries
    {
        private readonly Dictionary<MonitoredRepositoryId, string> _slugs = [];

        public void AddSlug(MonitoredRepositoryId repositoryId, string slug)
        {
            _slugs[repositoryId] = slug;
        }

        public Task<IReadOnlyDictionary<MonitoredRepositoryId, string>> GetSlugsAsync(
            IReadOnlySet<MonitoredRepositoryId> repositoryIds,
            CancellationToken cancellationToken)
        {
            Dictionary<MonitoredRepositoryId, string> result = repositoryIds
                .Where(id => _slugs.ContainsKey(id))
                .ToDictionary(id => id, id => _slugs[id]);

            return Task.FromResult<IReadOnlyDictionary<MonitoredRepositoryId, string>>(result);
        }

        public Task<string?> GetProviderTypeAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }
}
