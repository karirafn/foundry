using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistRevisionFailedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistRevisionFailedIssue()
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
    public async Task WhenRevisionFailed_CanBeReloadedWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Guid workerRunId = Guid.NewGuid();
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero);
        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Please fix the formatting."),
            new ReviewComment("Rename this variable.", "src/Foo.cs", 42),
        ];
        RevisionFailedIssue revisionFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(61)
            .WithTitle("Revision failed issue")
            .WithBody("Body")
            .WithLabels([])
            .WithWorkerRunId(workerRunId)
            .WithBranchName("feat/issue-61")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/21")
            .WithReviewComments(comments)
            .WithFailureReason("Container exited with code 1")
            .WithFailureCategory("generic_failure")
            .WithFailedAt(failedAt)
            .RevisionFailed();

        _dbContext.Set<Issue>().Add(revisionFailed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([revisionFailed.Id], TestContext.Current.CancellationToken);

        // Assert
        RevisionFailedIssue reloaded = result.ShouldBeOfType<RevisionFailedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(workerRunId),
            () => reloaded.BranchName.ShouldBe("feat/issue-61"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/21"),
            () => reloaded.ReviewComments.Count.ShouldBe(2),
            () => reloaded.ReviewComments[0].Body.ShouldBe("Please fix the formatting."),
            () => reloaded.ReviewComments[1].Body.ShouldBe("Rename this variable."),
            () => reloaded.ReviewComments[1].FilePath.ShouldBe("src/Foo.cs"),
            () => reloaded.ReviewComments[1].Line.ShouldBe(42),
            () => reloaded.FailureReason.ShouldBe("Container exited with code 1"),
            () => reloaded.FailedAt.ShouldBe(failedAt),
            () => reloaded.Author.Value.ShouldBe("octocat"),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
