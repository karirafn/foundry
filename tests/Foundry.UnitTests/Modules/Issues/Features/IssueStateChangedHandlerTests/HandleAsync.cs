using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssueStateChangedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly StubIssueBroadcaster _broadcaster;
    private readonly IssueStateChangedHandler _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _broadcaster = new StubIssueBroadcaster();
        StubRepositorySlugQueries slugQueries = new();
        IIssueQueries issueQueries = new IssueQueries(_dbContext, slugQueries);
        _sut = new IssueStateChangedHandler(issueQueries, _broadcaster);
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
    public async Task WhenIssueStateChanged_BroadcastsIssueSummary()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        IssueQueued @event = new(issue.Id, repositoryId);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary summary = _broadcaster.BroadcastedSummary.ShouldNotBeNull();
        summary.Id.ShouldBe(issue.Id.Value);
    }

    [Fact]
    public async Task WhenIssueNotFound_DoesNotBroadcast()
    {
        // Arrange
        IssueQueued @event = new(IssueId.New(), MonitoredRepositoryId.New());

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.BroadcastedSummary.ShouldBeNull();
    }

    private sealed class StubIssueBroadcaster : IIssueBroadcaster
    {
        public IssueSummary? BroadcastedSummary { get; private set; }

        public Task BroadcastAsync(IssueSummary summary, CancellationToken cancellationToken)
        {
            BroadcastedSummary = summary;
            return Task.CompletedTask;
        }
    }

    private sealed class StubRepositorySlugQueries : IRepositorySlugQueries
    {
        public Task<IReadOnlyDictionary<MonitoredRepositoryId, string>> GetSlugsAsync(
            IReadOnlySet<MonitoredRepositoryId> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<MonitoredRepositoryId, string>>(
                new Dictionary<MonitoredRepositoryId, string>());
    }
}
