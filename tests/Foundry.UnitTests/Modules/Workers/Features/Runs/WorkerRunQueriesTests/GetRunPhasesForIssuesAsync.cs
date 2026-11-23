using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.WorkerRunQueriesTests;

public sealed class GetRunPhasesForIssuesAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetRunPhasesForIssuesAsync()
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

    private static StartingRun Starting(IssueId issueId) => StartingRun.Begin(issueId, WorkerRunId.New());

    private static ActiveRun Active(IssueId issueId) =>
        Starting(issueId).Activate(
            ContainerId.From("container-" + Guid.NewGuid()),
            BranchName.From("feat/1-run"),
            MonitoredRepositoryId.New());

    private static PublishingRun Publishing(IssueId issueId) =>
        Active(issueId).BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, Now),
            containerOutput: null,
            verdict: null,
            pullRequestText: null);

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private Task<IReadOnlyDictionary<Guid, WorkerRunPhase>> QueryAsync(params IssueId[] issueIds) =>
        new WorkerRunQueries(_dbContext).GetRunPhasesForIssuesAsync(
            [.. issueIds.Select(id => id.Value)],
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenNoIssueIdsAreGiven_ReturnsNone()
    {
        // Arrange
        await SeedAsync(Starting(IssueId.New()));

        // Act
        IReadOnlyDictionary<Guid, WorkerRunPhase> result = await QueryAsync();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRunIsStarting_ReturnsStarting()
    {
        // Arrange
        IssueId starting = IssueId.New();
        await SeedAsync(Starting(starting));

        // Act
        IReadOnlyDictionary<Guid, WorkerRunPhase> result = await QueryAsync(starting);

        // Assert
        result.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<Guid, WorkerRunPhase>(starting.Value, WorkerRunPhase.Starting));
    }

    [Fact]
    public async Task WhenRunIsPublishing_ReturnsPublishing()
    {
        // Arrange
        IssueId publishing = IssueId.New();
        await SeedAsync(Publishing(publishing));

        // Act
        IReadOnlyDictionary<Guid, WorkerRunPhase> result = await QueryAsync(publishing);

        // Assert
        result.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<Guid, WorkerRunPhase>(publishing.Value, WorkerRunPhase.Publishing));
    }

    [Fact]
    public async Task WhenPublishFailedTransiently_ReturnsRetrying()
    {
        // Arrange
        IssueId retrying = IssueId.New();
        await SeedAsync(Publishing(retrying).FailPublish("provider down", transient: true, Now));

        // Act
        IReadOnlyDictionary<Guid, WorkerRunPhase> result = await QueryAsync(retrying);

        // Assert
        result.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<Guid, WorkerRunPhase>(retrying.Value, WorkerRunPhase.Retrying));
    }

    [Fact]
    public async Task OmitsIssuesWhoseRunIsActiveExhaustedPrepareFailedOrFinished()
    {
        // Arrange
        IssueId active = IssueId.New();
        IssueId exhausted = IssueId.New();
        IssueId prepareFailed = IssueId.New();
        IssueId completed = IssueId.New();
        IssueId noRun = IssueId.New();
        await SeedAsync(Active(active));
        await SeedAsync(Publishing(exhausted).FailPublish("invalid", transient: false, Now));
        await SeedAsync(Starting(prepareFailed).Fail(new FailureReason.ContainerError("prepare failed")));
        await SeedAsync(Active(completed).Complete(exitCode: 0, branchName: null, pullRequestUrl: null));

        // Act
        IReadOnlyDictionary<Guid, WorkerRunPhase> result = await QueryAsync(active, exhausted, prepareFailed, completed, noRun);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task OmitsIssuesThatWereNotAskedFor()
    {
        // Arrange
        IssueId asked = IssueId.New();
        await SeedAsync(Starting(asked));
        await SeedAsync(Starting(IssueId.New()));

        // Act
        IReadOnlyDictionary<Guid, WorkerRunPhase> result = await QueryAsync(asked);

        // Assert
        result.Keys.ShouldBe([asked.Value]);
    }
}
