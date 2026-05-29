using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Features.CreateIssueHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenIssueDetectedEventReceived_PersistsDetectedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 42,
            Title: "Fix the bug",
            Body: "Bug body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/42",
            Labels: ["bug"],
            DetectedAt: DateTimeOffset.UtcNow);

        IIntegrationEventHandler<IssueDetected> sut = new CreateIssueHandler(_dbContext);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        DetectedIssue detected = _dbContext.Set<Issue>()
            .OfType<DetectedIssue>()
            .ShouldHaveSingleItem();
        detected.ShouldSatisfyAllConditions(
            () => detected.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => detected.IssueNumber.ShouldBe(42),
            () => detected.Title.ShouldBe("Fix the bug"),
            () => detected.Body.ShouldBe("Bug body"),
            () => detected.Labels.ShouldBe(["bug"]));
    }

    [Fact]
    public async Task WhenIssueDetectedEventReceived_MapsAuthorAndUrl()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 7,
            Title: "New issue",
            Body: "Body",
            Author: "user",
            Url: "https://github.com/owner/repo/issues/7",
            Labels: [],
            DetectedAt: DateTimeOffset.UtcNow);

        IIntegrationEventHandler<IssueDetected> sut = new CreateIssueHandler(_dbContext);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        DetectedIssue detected = _dbContext.Set<Issue>()
            .OfType<DetectedIssue>()
            .ShouldHaveSingleItem();
        detected.ShouldSatisfyAllConditions(
            () => detected.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => detected.IssueNumber.ShouldBe(7));
    }
}
