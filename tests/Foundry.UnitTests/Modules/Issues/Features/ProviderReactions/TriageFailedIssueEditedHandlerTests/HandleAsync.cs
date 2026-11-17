using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.TriageFailedIssueEditedHandlerTests;

/// <summary>An issue edit sends a failed triage back to triage (issue #773).</summary>
public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset FailedAt = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEvents = new();
    private readonly TriageFailedIssueEditedHandler _sut;
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
        _sut = new TriageFailedIssueEditedHandler(
            _dbContext,
            _domainEvents,
            new FakeTimeProvider(FailedAt.AddMinutes(10)),
            NullLogger<TriageFailedIssueEditedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IssueBuilder Builder() =>
        new IssueBuilder().WithMonitoredRepositoryId(_repositoryId).WithIssueNumber(3).WithFailedAt(FailedAt);

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
    public async Task WhenTheIssueIsStillTriageFailed_QueuesItForTriage()
    {
        // Arrange
        Seed(Builder().TriageFailed());

        // Act
        await _sut.HandleAsync(
            new TriageFailedIssueEdited(_repositoryId, 3, FailedAt.AddMinutes(5)),
            TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync()).ShouldBeOfType<TriageQueuedIssue>();
        _domainEvents.DispatchedEvents.OfType<IssueTriageQueued>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenAnOperatorAlreadyRetriedTriage_DoesNothing()
    {
        // Arrange
        Issue retried = Builder().TriageFailed().RetryTriage(FailedAt.AddMinutes(1));
        Seed(retried);

        // Act
        await _sut.HandleAsync(
            new TriageFailedIssueEdited(_repositoryId, 3, FailedAt.AddMinutes(5)),
            TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync()).ShouldBeOfType<TriageQueuedIssue>().EnteredStateAt.ShouldBe(FailedAt.AddMinutes(1));
    }

    [Fact]
    public async Task WhenAnOperatorAlreadySkippedTriage_DoesNothing()
    {
        // Arrange
        Seed(Builder().TriageFailed().SkipTriage(FailedAt.AddMinutes(1)));

        // Act
        await _sut.HandleAsync(
            new TriageFailedIssueEdited(_repositoryId, 3, FailedAt.AddMinutes(5)),
            TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync()).ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenTheEditWasSeenBeforeTheCurrentFailure_DoesNothing()
    {
        // Arrange
        Seed(Builder().TriageFailed());

        // Act
        await _sut.HandleAsync(
            new TriageFailedIssueEdited(_repositoryId, 3, FailedAt.AddMinutes(-5)),
            TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync()).ShouldBeOfType<TriageFailedIssue>();
    }

    [Fact]
    public async Task WhenTheIssueIsUnknown_DoesNothing()
    {
        // Act
        await _sut.HandleAsync(
            new TriageFailedIssueEdited(_repositoryId, 3, FailedAt.AddMinutes(5)),
            TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<Issue>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }
}
