using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistUnchangedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistUnchangedIssue()
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

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    [Fact]
    public async Task WhenUnchangedIssueTransitioned_CanBeReloadedAsUnchangedIssueWithWorkerRunId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 43,
            title: "Unchanged issue",
            body: "Unchanged body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid unchangedWorkerRunId = Guid.NewGuid();
        UnchangedIssue unchanged = inProgress.MarkUnchanged(unchangedWorkerRunId);
        await _dbContext.TransitionAsync(inProgress, unchanged, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([unchanged.Id], TestContext.Current.CancellationToken);

        // Assert
        UnchangedIssue reloaded = result.ShouldBeOfType<UnchangedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(unchangedWorkerRunId),
            () => reloaded.Author.Value.ShouldBe(ValidAuthor.Value),
            () => reloaded.Url.Value.ShouldBe(ValidUrl.Value));
    }
}
