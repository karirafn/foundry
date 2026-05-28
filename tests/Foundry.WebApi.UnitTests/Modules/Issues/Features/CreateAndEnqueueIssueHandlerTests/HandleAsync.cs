using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Issues.Features;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Infrastructure;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Features.CreateAndEnqueueIssueHandlerTests;

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

    private static IDomainEventDispatcher BuildDispatcher(params IDomainEventHandler<IssueQueued>[] handlers)
    {
        ServiceCollection services = new();
        foreach (IDomainEventHandler<IssueQueued> handler in handlers)
        {
            services.AddSingleton(handler);
        }

        return new DomainEventDispatcher(services.BuildServiceProvider());
    }

    [Fact]
    public async Task WhenIssueDetectedEventReceived_PersistsQueuedIssue()
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

        IDomainEventDispatcher dispatcher = BuildDispatcher();
        IDomainEventHandler<IssueDetected> sut = new CreateAndEnqueueIssueHandler(_dbContext, dispatcher);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        QueuedIssue queued = _dbContext.Set<Issue>()
            .OfType<QueuedIssue>()
            .ShouldHaveSingleItem();
        queued.ShouldSatisfyAllConditions(
            () => queued.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => queued.IssueNumber.ShouldBe(42),
            () => queued.Title.ShouldBe("Fix the bug"),
            () => queued.Body.ShouldBe("Bug body"),
            () => queued.Labels.ShouldBe(["bug"]));
    }

    [Fact]
    public async Task WhenIssueDetectedEventReceived_DispatchesIssueQueuedEvent()
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

        List<IssueQueued> receivedEvents = [];
        IssueQueuedCapturingHandler capturingHandler = new(receivedEvents);
        IDomainEventDispatcher dispatcher = BuildDispatcher(capturingHandler);
        IDomainEventHandler<IssueDetected> sut = new CreateAndEnqueueIssueHandler(_dbContext, dispatcher);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        IssueQueued queued = receivedEvents.ShouldHaveSingleItem();
        queued.MonitoredRepositoryId.ShouldBe(repositoryId);
    }

    private sealed class IssueQueuedCapturingHandler(List<IssueQueued> received) : IDomainEventHandler<IssueQueued>
    {
        public Task HandleAsync(IssueQueued @event, CancellationToken cancellationToken)
        {
            received.Add(@event);
            return Task.CompletedTask;
        }
    }
}
