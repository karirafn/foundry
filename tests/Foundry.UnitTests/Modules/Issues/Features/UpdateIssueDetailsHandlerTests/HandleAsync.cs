using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.UpdateIssueDetailsHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<IssueDetailsChanged> _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new UpdateIssueDetailsHandler(_dbContext);
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

    [Fact]
    public async Task WhenIssueExists_UpdatesTitleBodyAndLabels()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 5,
            title: "Original title",
            body: "Original body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["old-label"],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        IssueDetailsChanged @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 5,
            Title: "Updated title",
            Body: "Updated body",
            Labels: ["new-label"]);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? updated = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId && i.IssueNumber == 5,
                TestContext.Current.CancellationToken);

        Issue loadedIssue = updated.ShouldNotBeNull();
        loadedIssue.ShouldSatisfyAllConditions(
            () => loadedIssue.Title.ShouldBe("Updated title"),
            () => loadedIssue.Body.ShouldBe("Updated body"),
            () => loadedIssue.Labels.ShouldBe(["new-label"]));
    }

    [Fact]
    public async Task WhenIssueDoesNotExist_DoesNotThrow()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueDetailsChanged @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 999,
            Title: "Title",
            Body: "Body",
            Labels: []);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task WhenIssueExistsInDifferentRepository_DoesNotUpdateIt()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        DetectedIssue issue = DetectedIssue.Detect(
            otherRepo,
            issueNumber: 5,
            title: "Original title",
            body: "Original body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        IssueDetailsChanged @event = new(
            MonitoredRepositoryId: targetRepo,
            IssueNumber: 5,
            Title: "Updated title",
            Body: "Updated body",
            Labels: []);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? unchanged = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == otherRepo && i.IssueNumber == 5,
                TestContext.Current.CancellationToken);

        unchanged.ShouldNotBeNull().Title.ShouldBe("Original title");
    }
}
