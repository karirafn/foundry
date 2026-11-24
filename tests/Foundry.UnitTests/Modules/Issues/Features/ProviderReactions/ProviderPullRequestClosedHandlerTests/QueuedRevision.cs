using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.ProviderPullRequestClosedHandlerTests;

/// <summary>The pull request closed without merging while a revision waited to start, queued or held by blockers.</summary>
public sealed class QueuedRevision : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ProviderPullRequestClosedHandler _sut;

    public QueuedRevision()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new ProviderPullRequestClosedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            NullLogger<ProviderPullRequestClosedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public static TheoryData<string> QueuedRevisionStates() => ["revision_queued", "revision_blocked"];

    private Issue Seed(string state, MonitoredRepositoryId repositoryId)
    {
        IssueBuilder builder = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .WithPullRequestUrl("https://github.com/owner/repo/pull/70");
        Issue issue = state switch
        {
            "revision_queued" => builder.RevisionQueued(),
            "revision_blocked" => builder.RevisionBlocked([9]),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    [Theory]
    [MemberData(nameof(QueuedRevisionStates))]
    public async Task WhenThePullRequestClosesUnmergedBeforeTheRevisionStarts_TheIssueFailsContinuably(string state)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Issue issue = Seed(state, repositoryId);

        // Act
        await _sut.HandleAsync(new ProviderPullRequestClosed(repositoryId, 7), TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
        ContinuableFailedIssue failed = reloaded.ShouldBeOfType<ContinuableFailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureCategory.ShouldBe(FailureCategory.PrClosed),
            () => failed.BranchName.ShouldBe("feat/1-test"),
            () => failed.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/70"),
            () => failed.WorkerRunId.ShouldBeNull());
    }
}
