using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.StateChanges.MergeQueueChangedHandlerTests;

/// <summary>A catch-up queue change without a state change broadcasts each named issue, so an open pane reloads.</summary>
public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly StubMergeQueueQueries _mergeQueue = new();
    private readonly CapturingIssueBroadcaster _broadcaster = new();
    private readonly MergeQueueChangedHandler _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        IssueQueries issueQueries = new(
            _dbContext,
            new NullRepositorySlugQueries(),
            new NullRepositoryEligibilityQuery(),
            new NullWorkerRunQueries(),
            new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
            new NullGlobalSettingsQueries(),
            _mergeQueue);
        _sut = new MergeQueueChangedHandler(_dbContext, new IssueStateChangedHandler(issueQueries, _broadcaster));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task SeedMergePendingAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        _dbContext.Set<Issue>().Add(
            new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(issueNumber).MergePending());
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task BroadcastsEachNamedIssueOfTheRepository()
    {
        // Arrange
        await SeedMergePendingAsync(RepositoryId, 41);
        await SeedMergePendingAsync(RepositoryId, 42);
        await SeedMergePendingAsync(RepositoryId, 43);
        await SeedMergePendingAsync(MonitoredRepositoryId.New(), 42);

        // Act
        await _sut.HandleAsync(new MergeQueueChanged(RepositoryId, [42, 43, 99]), TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Summaries.Select(s => (s.IssueNumber, s.State)).ShouldBe([(42, "merge_pending"), (43, "merge_pending")]);
        _broadcaster.Summaries.ShouldAllBe(s => s.RepositoryId == RepositoryId.Value);
    }

    [Fact]
    public async Task WhenNoNamedIssueIsTracked_BroadcastsNothing()
    {
        // Arrange
        await SeedMergePendingAsync(RepositoryId, 41);

        // Act
        await _sut.HandleAsync(new MergeQueueChanged(RepositoryId, [99]), TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Summaries.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenANamedIssueWaitsToCatchUp_ItsBroadcastCarriesTheCatchUpWaitingStep()
    {
        // Arrange
        await SeedMergePendingAsync(RepositoryId, 41);
        _mergeQueue.Waiting.Add((RepositoryId, 41));

        // Act
        await _sut.HandleAsync(new MergeQueueChanged(RepositoryId, [41]), TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Summaries.ShouldHaveSingleItem().CodeStep.ShouldBe(IssueSummary.CatchUpWaitingStep);
    }

    private sealed class CapturingIssueBroadcaster : IIssueBroadcaster
    {
        public List<IssueSummary> Summaries { get; } = [];

        public Task BroadcastAsync(IssueSummary summary, CancellationToken cancellationToken)
        {
            Summaries.Add(summary);
            return Task.CompletedTask;
        }
    }
}
