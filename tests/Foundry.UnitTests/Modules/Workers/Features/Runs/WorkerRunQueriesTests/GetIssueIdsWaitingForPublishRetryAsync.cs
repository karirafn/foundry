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

public sealed class GetIssueIdsWaitingForPublishRetryAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetIssueIdsWaitingForPublishRetryAsync()
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

    private static ActiveRun Active(IssueId issueId) =>
        StartingRun.Begin(issueId, WorkerRunId.New()).Activate(
            ContainerId.From("container-" + Guid.NewGuid()),
            BranchName.From("feat/1-run"),
            MonitoredRepositoryId.New());

    private static PublishingRun Publishing(IssueId issueId) =>
        Active(issueId).BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, Now),
            containerOutput: null,
            verdict: null,
            pullRequestText: null);

    private static PublishFailedRun Retrying(IssueId issueId) =>
        Publishing(issueId).FailPublish("provider down", transient: true, Now);

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private Task<IReadOnlySet<Guid>> QueryAsync(params IssueId[] issueIds) =>
        new WorkerRunQueries(_dbContext).GetIssueIdsWaitingForPublishRetryAsync(
            [.. issueIds.Select(id => id.Value)],
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenNoIssueIdsAreGiven_ReturnsNone()
    {
        // Arrange
        await SeedAsync(Retrying(IssueId.New()));

        // Act
        IReadOnlySet<Guid> result = await QueryAsync();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReturnsTheIssuesWhoseRunIsWaitingToRetryOrExhausted()
    {
        // Arrange
        IssueId retrying = IssueId.New();
        IssueId exhausted = IssueId.New();
        await SeedAsync(Retrying(retrying));
        await SeedAsync(Publishing(exhausted).FailPublish("invalid", transient: false, Now));

        // Act
        IReadOnlySet<Guid> result = await QueryAsync(retrying, exhausted);

        // Assert
        result.ShouldBe([retrying.Value, exhausted.Value], ignoreOrder: true);
    }

    [Fact]
    public async Task OmitsIssuesWhoseRunIsStartingActivePublishingBlockedOrFinished()
    {
        // Arrange
        IssueId starting = IssueId.New();
        IssueId active = IssueId.New();
        IssueId publishing = IssueId.New();
        IssueId blocked = IssueId.New();
        IssueId completed = IssueId.New();
        IssueId noRun = IssueId.New();
        await SeedAsync(StartingRun.Begin(starting, WorkerRunId.New()));
        await SeedAsync(Active(active));
        await SeedAsync(Publishing(publishing));
        await SeedAsync(Publishing(blocked).BlockPublish(
            [new SecretFinding("src/appsettings.json", 14, CommitSha.From("3333333333333333333333333333333333333333"), "aws-access-token")],
            Now));
        await SeedAsync(Active(completed).Complete(exitCode: 0, branchName: null, pullRequestUrl: null));

        // Act
        IReadOnlySet<Guid> result = await QueryAsync(starting, active, publishing, blocked, completed, noRun);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task OmitsIssuesThatWereNotAskedFor()
    {
        // Arrange
        IssueId asked = IssueId.New();
        await SeedAsync(Retrying(asked));
        await SeedAsync(Retrying(IssueId.New()));

        // Act
        IReadOnlySet<Guid> result = await QueryAsync(asked);

        // Assert
        result.ShouldBe([asked.Value]);
    }
}
