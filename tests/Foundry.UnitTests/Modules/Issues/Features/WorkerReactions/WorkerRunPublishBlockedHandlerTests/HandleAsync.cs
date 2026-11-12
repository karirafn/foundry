using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunPublishBlockedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly WorkerRunPublishBlockedHandler _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new WorkerRunPublishBlockedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            TimeProvider.System,
            NullLogger<WorkerRunPublishBlockedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task AnImplementRunBlocked_MovesTheIssueToPublishBlocked()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());

        // Act
        await _sut.HandleAsync(
            new WorkerRunPublishBlocked(inProgress.WorkerRunId, inProgress.Id.Value, "feat/1-test", FindingCount: 2),
            TestContext.Current.CancellationToken);

        // Assert
        PublishBlockedIssue blocked = (await ReloadAsync(inProgress)).ShouldBeOfType<PublishBlockedIssue>();
        blocked.ShouldSatisfyAllConditions(
            () => blocked.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => blocked.BranchName.ShouldBe("feat/1-test"));
    }

    [Fact]
    public async Task ARevisionBlocked_MovesTheIssueToPublishBlockedAsARevision()
    {
        // Arrange
        RevisionInProgressIssue revision = Seed(new IssueBuilder()
            .WithBranchName("feat/2-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .RevisionInProgress());

        // Act
        await _sut.HandleAsync(
            new WorkerRunPublishBlocked(revision.WorkerRunId, revision.Id.Value, "feat/2-fix", FindingCount: 1),
            TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(revision)).ShouldBeOfType<PublishBlockedIssue>().IsRevision.ShouldBeTrue();
    }

    [Fact]
    public async Task AStaleRun_LeavesTheIssueUnchanged()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());

        // Act
        await _sut.HandleAsync(
            new WorkerRunPublishBlocked(WorkerRunId.New(), inProgress.Id.Value, "feat/1-test", FindingCount: 1),
            TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(inProgress)).ShouldBeOfType<InProgressIssue>();
    }

    private T Seed<T>(T issue)
        where T : Issue
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == issue.Id, TestContext.Current.CancellationToken);
    }
}
