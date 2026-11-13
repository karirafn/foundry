using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunPublishRetryRequestedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunPublishRetryRequested> _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new WorkerRunPublishRetryRequestedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            TimeProvider.System,
            NullLogger<WorkerRunPublishRetryRequestedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
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

    [Fact]
    public async Task WhenImplementPublishIsRetried_ReturnsToInProgressWithTheSameRun()
    {
        // Arrange
        PublishFailedIssue publishFailed = Seed(new IssueBuilder().PublishFailed());

        // Act
        await _sut.HandleAsync(
            new WorkerRunPublishRetryRequested(publishFailed.WorkerRunId, publishFailed.Id.Value),
            TestContext.Current.CancellationToken);

        // Assert
        InProgressIssue inProgress = (await ReloadAsync(publishFailed)).ShouldBeOfType<InProgressIssue>();
        inProgress.WorkerRunId.ShouldBe(publishFailed.WorkerRunId);
    }

    [Fact]
    public async Task WhenRevisionPublishIsRetried_ReturnsToRevisionInProgress()
    {
        // Arrange
        PublishFailedIssue publishFailed = Seed(new IssueBuilder().PublishFailedFromRevision());

        // Act
        await _sut.HandleAsync(
            new WorkerRunPublishRetryRequested(publishFailed.WorkerRunId, publishFailed.Id.Value),
            TestContext.Current.CancellationToken);

        // Assert
        RevisionInProgressIssue revision = (await ReloadAsync(publishFailed)).ShouldBeOfType<RevisionInProgressIssue>();
        revision.WorkerRunId.ShouldBe(publishFailed.WorkerRunId);
    }

    [Fact]
    public async Task WhenRetryNamesAnotherRun_LeavesPublishFailed()
    {
        // Arrange
        PublishFailedIssue publishFailed = Seed(new IssueBuilder().PublishFailed());

        // Act
        await _sut.HandleAsync(
            new WorkerRunPublishRetryRequested(WorkerRunId.New(), publishFailed.Id.Value),
            TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(publishFailed)).ShouldBeOfType<PublishFailedIssue>();
    }

    [Fact]
    public async Task WhenIssueIsNotPublishFailed_IgnoresTheRequest()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());

        // Act
        await _sut.HandleAsync(
            new WorkerRunPublishRetryRequested(inProgress.WorkerRunId, inProgress.Id.Value),
            TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(inProgress)).ShouldBeOfType<InProgressIssue>();
    }
}
