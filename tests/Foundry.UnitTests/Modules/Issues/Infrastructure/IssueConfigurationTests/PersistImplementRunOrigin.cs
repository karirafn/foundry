using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

/// <summary>
/// The implement run origin (#1060) survives a save and reload on the running issue and on the publish states that
/// hold it until the run resumes.
/// </summary>
public sealed class PersistImplementRunOrigin : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistImplementRunOrigin()
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
    public async Task AFreshRun_ReloadsAsFresh()
    {
        // Arrange
        InProgressIssue inProgress = new IssueBuilder().InProgress();

        // Act
        Issue? result = await SaveAndReloadAsync(inProgress);

        // Assert
        result.ShouldBeOfType<InProgressIssue>().Origin.ShouldBe(ImplementRunOrigin.Fresh);
    }

    [Fact]
    public async Task AContinuationRun_ReloadsAsContinuation()
    {
        // Arrange
        InProgressIssue inProgress = ContinuationRun();

        // Act
        Issue? result = await SaveAndReloadAsync(inProgress);

        // Assert
        result.ShouldBeOfType<InProgressIssue>().Origin.ShouldBe(ImplementRunOrigin.Continuation);
    }

    [Fact]
    public async Task ABlockedContinuation_ReloadsWithItsOrigin()
    {
        // Arrange
        PublishBlockedIssue blocked = ContinuationRun().MarkPublishBlocked("feat/1-test", Now);

        // Act
        Issue? result = await SaveAndReloadAsync(blocked);

        // Assert
        result.ShouldBeOfType<PublishBlockedIssue>().Origin.ShouldBe(ImplementRunOrigin.Continuation);
    }

    [Fact]
    public async Task AFailedPublishOfAContinuation_ReloadsWithItsOrigin()
    {
        // Arrange
        PublishFailedIssue publishFailed = ContinuationRun().MarkPublishFailed("feat/1-test", "push refused", Now);

        // Act
        Issue? result = await SaveAndReloadAsync(publishFailed);

        // Assert
        result.ShouldBeOfType<PublishFailedIssue>().Origin.ShouldBe(ImplementRunOrigin.Continuation);
    }

    [Fact]
    public async Task ABlockedRevision_ReloadsWithoutAnOrigin()
    {
        // Arrange
        PublishBlockedIssue blocked = new IssueBuilder().RevisionInProgress().MarkPublishBlocked(Now);

        // Act
        Issue? result = await SaveAndReloadAsync(blocked);

        // Assert
        result.ShouldBeOfType<PublishBlockedIssue>().Origin.ShouldBeNull();
    }

    private static InProgressIssue ContinuationRun() =>
        new IssueBuilder().ContinuableFailed().Retry(Now).Claim(WorkerRunId.New(), Now);

    private async Task<Issue?> SaveAndReloadAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
    }
}
