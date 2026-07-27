using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.CreateIssueHandlerTests;

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
            IssueKindLabel: "bug",
            DetectedAt: DateTimeOffset.UtcNow);

        IIntegrationEventHandler<IssueDetected> sut = new CreateIssueHandler(_dbContext, NullLogger<CreateIssueHandler>.Instance);

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
    public async Task WhenIssueDetectedWithBugKindLabel_SetsIssueKindToBug()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueDetected @event = new(
            MonitoredRepositoryId: repositoryId,
            IssueNumber: 1,
            Title: "Bug fix",
            Body: "Body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/1",
            Labels: ["bug"],
            IssueKindLabel: "bug",
            DetectedAt: DateTimeOffset.UtcNow);

        IIntegrationEventHandler<IssueDetected> sut = new CreateIssueHandler(_dbContext, NullLogger<CreateIssueHandler>.Instance);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        DetectedIssue detected = _dbContext.Set<Issue>()
            .OfType<DetectedIssue>()
            .ShouldHaveSingleItem();
        detected.IssueKind.ShouldBe(IssueKind.Bug);
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
            IssueKindLabel: "feature",
            DetectedAt: DateTimeOffset.UtcNow);

        IIntegrationEventHandler<IssueDetected> sut = new CreateIssueHandler(_dbContext, NullLogger<CreateIssueHandler>.Instance);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        DetectedIssue detected = _dbContext.Set<Issue>()
            .OfType<DetectedIssue>()
            .ShouldHaveSingleItem();
        detected.ShouldSatisfyAllConditions(
            () => detected.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => detected.IssueNumber.ShouldBe(7),
            () => detected.Author.Value.ShouldBe("user"),
            () => detected.Url.Value.ToString().ShouldBe("https://github.com/owner/repo/issues/7"));
    }
}
