using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunCompletedHandlerTests;

public sealed class RevisionRunIdMismatch : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<WorkerRunCompleted> _sut;

    public RevisionRunIdMismatch()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingDomainEventDispatcher();
        _sut = new WorkerRunCompletedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<WorkerRunCompletedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private RevisionInProgressIssue SeedRevisionInProgressIssue(MonitoredRepositoryId repositoryId)
    {
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .WithBranchName("feat/issue-1")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .RevisionInProgress();
        _dbContext.Set<Issue>().Add(revisionInProgress);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return revisionInProgress;
    }

    [Fact]
    public async Task WhenRunIdDiffersFromRevisionInProgressRunId_IssueRemainsRevisionInProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId);
        Guid staleRunId = Guid.NewGuid();

        WorkerRunCompleted @event = new(
            WorkerRunId: staleRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: revisionInProgress.BranchName,
            PullRequestUrl: revisionInProgress.PullRequestUrl,
            MergeState: WorkerRunMergeState.Open);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        RevisionInProgressIssue stillRevisionInProgress = issue.ShouldBeOfType<RevisionInProgressIssue>();
        stillRevisionInProgress.WorkerRunId.ShouldBe(revisionInProgress.WorkerRunId);
    }

    [Fact]
    public async Task WhenRunIdDiffersFromRevisionInProgressRunId_NothingIsDispatched()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = SeedRevisionInProgressIssue(repositoryId);
        Guid staleRunId = Guid.NewGuid();

        WorkerRunCompleted @event = new(
            WorkerRunId: staleRunId,
            IssueId: revisionInProgress.Id.Value,
            BranchName: revisionInProgress.BranchName,
            PullRequestUrl: revisionInProgress.PullRequestUrl,
            MergeState: WorkerRunMergeState.Open);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }
}
