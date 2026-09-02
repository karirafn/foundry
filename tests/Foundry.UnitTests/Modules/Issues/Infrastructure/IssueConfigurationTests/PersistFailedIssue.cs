using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistFailedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistFailedIssue()
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
    public void FailureReason_HasMaxLength500AndIsNotUnicode()
    {
        // Arrange
        IEntityType entityType = _dbContext.Model.FindEntityType(typeof(FailedIssue))!;
        IProperty property = entityType.FindProperty(nameof(FailedIssue.FailureReason))!;

        // Act / Assert
        property.ShouldSatisfyAllConditions(
            () => property.GetMaxLength().ShouldBe(500),
            () => property.IsUnicode().ShouldBe(false));
    }

    [Fact]
    public async Task WhenFailedIssueTransitioned_CanBeReloadedAsFailedIssueWithAllFields()
    {
        // Arrange
        WorkerRunId failedWorkerRunId = WorkerRunId.New();
        DateTimeOffset failedAt = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        FailedIssue failed = new IssueBuilder()
            .WithIssueNumber(44)
            .WithTitle("Failed issue")
            .WithBody("Failed body")
            .WithLabels([])
            .WithWorkerRunId(failedWorkerRunId)
            .WithFailureReason("Container exited with code 1")
            .WithFailedAt(failedAt)
            .WithFailureCategory("generic_failure")
            .Failed();

        _dbContext.Set<Issue>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([failed.Id], TestContext.Current.CancellationToken);

        // Assert
        FailedIssue reloaded = result.ShouldBeOfType<FailedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(failedWorkerRunId),
            () => reloaded.FailureReason.ShouldBe("Container exited with code 1"),
            () => reloaded.FailedAt.ShouldBe(failedAt),
            () => reloaded.Author.Value.ShouldBe("octocat"),
            () => reloaded.Url.Value.ShouldBe(new Uri("https://github.com/owner/repo/issues/1")));
    }
}
