using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueSnapshotsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssuesModule _sut;

    public GetIssueSnapshotsAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssuesModule(_dbContext);
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

    [Fact]
    public async Task WhenNoIssuesMatch_ReturnsEmptyDictionary()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IReadOnlyDictionary<int, IssueSnapshot> result = await _sut.GetIssueSnapshotsAsync(
            repositoryId,
            new HashSet<int> { 1, 2 },
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssuesMatch_ReturnsSnapshotsKeyedByIssueNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 5,
            title: "My Issue",
            body: "Issue body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["bug", "foundry"],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyDictionary<int, IssueSnapshot> result = await _sut.GetIssueSnapshotsAsync(
            repositoryId,
            new HashSet<int> { 5 },
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(1);
        IssueSnapshot snapshot = result[5];
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Title.ShouldBe("My Issue"),
            () => snapshot.Body.ShouldBe("Issue body"),
            () => snapshot.Labels.ShouldBe(["bug", "foundry"]));
    }

    [Fact]
    public async Task WhenRequestedNumberNotInRepo_IsOmittedFromResult()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 3,
            title: "Issue 3",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyDictionary<int, IssueSnapshot> result = await _sut.GetIssueSnapshotsAsync(
            repositoryId,
            new HashSet<int> { 3, 99 },
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(1);
        result.ContainsKey(3).ShouldBeTrue();
        result.ContainsKey(99).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenIssuesBelongToDifferentRepo_AreOmittedFromResult()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        DetectedIssue targetIssue = DetectedIssue.Detect(
            targetRepo,
            issueNumber: 1,
            title: "Target",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        DetectedIssue otherIssue = DetectedIssue.Detect(
            otherRepo,
            issueNumber: 1,
            title: "Other",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().AddRange(targetIssue, otherIssue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyDictionary<int, IssueSnapshot> result = await _sut.GetIssueSnapshotsAsync(
            targetRepo,
            new HashSet<int> { 1 },
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(1);
        result[1].Title.ShouldBe("Target");
    }
}
