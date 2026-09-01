using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
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

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ProviderPullRequestClosedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;
    private readonly IIntegrationEventHandler<ProviderPullRequestClosed> _sut;

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
        _sut = new ProviderPullRequestClosedHandler(
            _dbContext,
            _dispatcher,
            NullLogger<ProviderPullRequestClosedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ReviewIssue SeedReviewIssue(MonitoredRepositoryId repositoryId, int issueNumber = 1)
    {
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .Review();
        _dbContext.Set<Issue>().Add(review);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return review;
    }

    private FreshQueuedIssue SeedQueuedIssue(MonitoredRepositoryId repositoryId, int issueNumber = 2)
    {
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .FreshQueued();
        _dbContext.Set<Issue>().Add(queued);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return queued;
    }

    [Fact]
    public async Task WhenReviewIssue_TransitionsToContinuableFailedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 1);

        ProviderPullRequestClosed @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 1);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        ContinuableFailedIssue failed = issue.ShouldBeOfType<ContinuableFailedIssue>();
        failed.FailureReason.ShouldBe("Pull request closed without merge");
    }

    [Fact]
    public async Task WhenNotReviewIssue_SilentlyIgnores()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedQueuedIssue(repositoryId, issueNumber: 2);

        ProviderPullRequestClosed @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 2);

        // Act
        Task act = _sut.HandleAsync(@event, CancellationToken.None);

        // Assert — does not throw; issue remains in original state
        await Should.NotThrowAsync(act);
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.MonitoredRepositoryId == repositoryId,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenReviewIssuePrIsClosed_DispatchesIssueContinuableFailedEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        SeedReviewIssue(repositoryId, issueNumber: 3);

        ProviderPullRequestClosed @event = new(
            RepositoryId: repositoryId,
            IssueNumber: 3);

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _dispatcher.DispatchedEvents
            .OfType<IssueContinuableFailed>()
            .ShouldHaveSingleItem();
    }
}
