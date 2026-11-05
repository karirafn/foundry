using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.RebaseResolvedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<RebaseResolved> _sut;

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
        _sut = new RebaseResolvedHandler(_dbContext, _dispatcher, NullLogger<RebaseResolvedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private RebasePendingIssue SeedRebasePendingIssue(MonitoredRepositoryId repositoryId, int issueNumber = 1)
    {
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithBranchName("feat/1-test-issue")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .RebasePending();
        _dbContext.Set<Issue>().Add(rebasePending);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return rebasePending;
    }

    private async Task<Issue?> LoadAsync(MonitoredRepositoryId repositoryId)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.MonitoredRepositoryId == repositoryId, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("up_to_date", typeof(RebaseOutcome.UpToDate))]
    [InlineData("rebased", typeof(RebaseOutcome.Rebased))]
    [InlineData("disabled", typeof(RebaseOutcome.Disabled))]
    [InlineData("conflict", typeof(RebaseOutcome.Conflict))]
    public async Task WhenRebasePending_TransitionsToChecksPendingWithOutcome(string outcome, Type expectedType)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRebasePendingIssue(repositoryId);

        // Act
        await _sut.HandleAsync(new RebaseResolved(repositoryId, 1, outcome), TestContext.Current.CancellationToken);

        // Assert
        ChecksPendingIssue checksPending = (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>();
        checksPending.RebaseOutcome.ShouldNotBeNull().GetType().ShouldBe(expectedType);
    }

    [Fact]
    public async Task WhenSkipped_PersistsTheReason()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRebasePendingIssue(repositoryId);

        // Act
        await _sut.HandleAsync(
            new RebaseResolved(repositoryId, 1, "skipped", RebaseSkipReasons.Timeout),
            TestContext.Current.CancellationToken);

        // Assert
        ChecksPendingIssue checksPending = (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>();
        checksPending.RebaseOutcome.ShouldBe(new RebaseOutcome.Skipped(RebaseSkipReasons.Timeout));
    }

    [Fact]
    public async Task WhenUnknownOutcome_FailsOpenAsSkipped()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRebasePendingIssue(repositoryId);

        // Act
        await _sut.HandleAsync(new RebaseResolved(repositoryId, 1, "mystery"), TestContext.Current.CancellationToken);

        // Assert
        ChecksPendingIssue checksPending = (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>();
        checksPending.RebaseOutcome.ShouldBeOfType<RebaseOutcome.Skipped>();
    }

    [Fact]
    public async Task WhenRebasePending_DispatchesIssueChecksPending()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedRebasePendingIssue(repositoryId);

        // Act
        await _sut.HandleAsync(new RebaseResolved(repositoryId, 1, "rebased"), TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.OfType<IssueChecksPending>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenIssueIsNotRebasePending_IsIgnored()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue checksPending = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ChecksPending();
        _dbContext.Set<Issue>().Add(checksPending);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await _sut.HandleAsync(new RebaseResolved(repositoryId, 1, "rebased"), TestContext.Current.CancellationToken);

        // Assert
        ChecksPendingIssue unchanged = (await LoadAsync(repositoryId)).ShouldBeOfType<ChecksPendingIssue>();
        unchanged.RebaseOutcome.ShouldBeOfType<RebaseOutcome.UpToDate>();
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }
}
