using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.CredentialReactions;
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

namespace Foundry.UnitTests.Modules.Issues.Features.CredentialReactions.CreditsRestoredHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<CreditsRestored> _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingDomainEventDispatcher();
        _sut = new CreditsRestoredHandler(
            _dbContext,
            _dispatcher,
            NullLogger<CreditsRestoredHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<FailedIssue> SeedFailedIssueAsync(
        MonitoredRepositoryId repositoryId,
        string failureReason,
        int issueNumber = 1)
    {
        FailedIssue failed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithFailureReason(failureReason)
            .WithFailureCategory("credits_exhausted")
            .Failed();
        _dbContext.Set<Issue>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    private async Task<ContinuableFailedIssue> SeedContinuableFailedIssueAsync(
        MonitoredRepositoryId repositoryId,
        string failureReason,
        int issueNumber = 1)
    {
        ContinuableFailedIssue continuableFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithBranchName("feat/issue-branch")
            .WithFailureReason(failureReason)
            .WithFailureCategory("credits_exhausted")
            .ContinuableFailed();
        _dbContext.Set<Issue>().Add(continuableFailed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return continuableFailed;
    }

    [Fact]
    public async Task WhenFailedIssueIsCreditsExhausted_TransitionsToQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        await SeedFailedIssueAsync(repositoryId, WorkerRunFailed.CreditsExhaustedReason);

        CreditsRestored @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenContinuableFailedIssueIsCreditsExhausted_TransitionsToContinuationQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        await SeedContinuableFailedIssueAsync(repositoryId, WorkerRunFailed.CreditsExhaustedReason);

        CreditsRestored @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<ContinuationQueuedIssue>();
    }

    [Fact]
    public async Task WhenFailedIssueHasDifferentFailureReason_RemainsAsFailed()
    {
        // Arrange — use a usage-limit reason to guard the exact-match constraint
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        await SeedFailedIssueAsync(repositoryId, WorkerRunFailed.UsageLimitedReason);

        CreditsRestored @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenContinuableFailedIssueHasDifferentFailureReason_RemainsAsContinuableFailed()
    {
        // Arrange — use a usage-limit reason to guard the exact-match constraint
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        await SeedContinuableFailedIssueAsync(repositoryId, WorkerRunFailed.UsageLimitedReason);

        CreditsRestored @event = new();

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<ContinuableFailedIssue>();
    }

    [Fact]
    public async Task WhenNoFailedIssues_HandlesGracefully()
    {
        // Arrange
        CreditsRestored @event = new();

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }
}
