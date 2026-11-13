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

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunFalsePositivesConfirmedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunFalsePositivesConfirmed> _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new WorkerRunFalsePositivesConfirmedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            TimeProvider.System,
            NullLogger<WorkerRunFalsePositivesConfirmedHandler>.Instance);
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
    public async Task WhenImplementRunsFindingsAreConfirmed_ReturnsToInProgressWithTheSameRun()
    {
        // Arrange
        PublishBlockedIssue blocked = Seed(new IssueBuilder().PublishBlocked());

        // Act
        await _sut.HandleAsync(
            new WorkerRunFalsePositivesConfirmed(blocked.WorkerRunId, blocked.Id.Value),
            TestContext.Current.CancellationToken);

        // Assert
        InProgressIssue inProgress = (await ReloadAsync(blocked)).ShouldBeOfType<InProgressIssue>();
        inProgress.WorkerRunId.ShouldBe(blocked.WorkerRunId);
    }

    [Fact]
    public async Task WhenRevisionsFindingsAreConfirmed_ReturnsToRevisionInProgress()
    {
        // Arrange
        PublishBlockedIssue blocked = Seed(new IssueBuilder().PublishBlockedFromRevision());

        // Act
        await _sut.HandleAsync(
            new WorkerRunFalsePositivesConfirmed(blocked.WorkerRunId, blocked.Id.Value),
            TestContext.Current.CancellationToken);

        // Assert
        RevisionInProgressIssue revision = (await ReloadAsync(blocked)).ShouldBeOfType<RevisionInProgressIssue>();
        revision.WorkerRunId.ShouldBe(blocked.WorkerRunId);
    }

    [Fact]
    public async Task WhenConfirmationNamesAnotherRun_LeavesPublishBlocked()
    {
        // Arrange
        PublishBlockedIssue blocked = Seed(new IssueBuilder().PublishBlocked());

        // Act
        await _sut.HandleAsync(
            new WorkerRunFalsePositivesConfirmed(WorkerRunId.New(), blocked.Id.Value),
            TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(blocked)).ShouldBeOfType<PublishBlockedIssue>();
    }

    [Fact]
    public async Task WhenIssueIsNotPublishBlocked_IgnoresTheConfirmation()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());

        // Act
        await _sut.HandleAsync(
            new WorkerRunFalsePositivesConfirmed(inProgress.WorkerRunId, inProgress.Id.Value),
            TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(inProgress)).ShouldBeOfType<InProgressIssue>();
    }
}
