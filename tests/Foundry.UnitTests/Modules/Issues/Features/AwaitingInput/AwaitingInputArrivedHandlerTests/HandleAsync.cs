using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.AwaitingInput;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.AwaitingInput.AwaitingInputArrivedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly AwaitingInputArrivedHandler _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new AwaitingInputArrivedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            TimeProvider.System,
            NullLogger<AwaitingInputArrivedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void Seed(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<Issue> LoadAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>().SingleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenTheQuestionIsPosted_ResumesWithAnInputProvidedRevisionAndPersistsTheAnsweredRound()
    {
        // Arrange
        Seed(new IssueBuilder().WithMonitoredRepositoryId(_repositoryId).WithIssueNumber(3).AwaitingInputPosted(PostedAt));

        // Act
        await _sut.HandleAsync(
            new AwaitingInputArrived(_repositoryId, 3, "Use Postgres.", PostedAt.AddMinutes(5)),
            TestContext.Current.CancellationToken);

        // Assert
        RevisionQueuedIssue queued = (await LoadAsync()).ShouldBeOfType<RevisionQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.Trigger.ShouldBeOfType<RevisionTrigger.InputProvided>(),
            () => queued.Clarifications.ShouldHaveSingleItem().Input.ShouldBe("Use Postgres."));
    }

    [Fact]
    public async Task WhenTheQuestionIsNotPostedYet_IgnoresTheEvent()
    {
        // Arrange
        Seed(new IssueBuilder().WithMonitoredRepositoryId(_repositoryId).WithIssueNumber(3).AwaitingInput());

        // Act
        await _sut.HandleAsync(
            new AwaitingInputArrived(_repositoryId, 3, "Use Postgres.", PostedAt.AddMinutes(5)),
            TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync()).ShouldBeOfType<AwaitingInputIssue>();
    }

    [Fact]
    public async Task WhenTheInputPredatesThePost_IgnoresTheEvent()
    {
        // Arrange
        Seed(new IssueBuilder().WithMonitoredRepositoryId(_repositoryId).WithIssueNumber(3).AwaitingInputPosted(PostedAt));

        // Act
        await _sut.HandleAsync(
            new AwaitingInputArrived(_repositoryId, 3, "An earlier round's answer.", PostedAt.AddMinutes(-5)),
            TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync()).ShouldBeOfType<AwaitingInputIssue>();
    }
}
