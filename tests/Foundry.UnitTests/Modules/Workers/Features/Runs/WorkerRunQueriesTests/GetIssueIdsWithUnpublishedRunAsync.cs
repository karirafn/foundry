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

public sealed class GetIssueIdsWithUnpublishedRunAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetIssueIdsWithUnpublishedRunAsync()
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

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private Task<IReadOnlySet<Guid>> QueryAsync(params IssueId[] issueIds) =>
        new WorkerRunQueries(_dbContext).GetIssueIdsWithUnpublishedRunAsync(
            [.. issueIds.Select(id => id.Value)],
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenNoIssueIdsAreGiven_ReturnsNone()
    {
        // Arrange
        await SeedAsync(Publishing(IssueId.New()));

        // Act
        IReadOnlySet<Guid> result = await QueryAsync();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReturnsTheIssuesWhoseRunIsPublishingRetryingExhaustedOrBlocked()
    {
        // Arrange
        IssueId publishing = IssueId.New();
        IssueId retrying = IssueId.New();
        IssueId exhausted = IssueId.New();
        IssueId blocked = IssueId.New();
        await SeedAsync(Publishing(publishing));
        await SeedAsync(Publishing(retrying).FailPublish("provider down", transient: true, Now));
        await SeedAsync(Publishing(exhausted).FailPublish("invalid", transient: false, Now));
        await SeedAsync(Publishing(blocked).BlockPublish(
            [new SecretFinding("src/appsettings.json", 14, CommitSha.From("3333333333333333333333333333333333333333"), "aws-access-token")],
            Now));

        // Act
        IReadOnlySet<Guid> result = await QueryAsync(publishing, retrying, exhausted, blocked);

        // Assert
        result.ShouldBe([publishing.Value, retrying.Value, exhausted.Value, blocked.Value], ignoreOrder: true);
    }

    [Fact]
    public async Task OmitsIssuesWhoseRunIsStartingActiveOrFinished()
    {
        // Arrange
        IssueId starting = IssueId.New();
        IssueId active = IssueId.New();
        IssueId completed = IssueId.New();
        IssueId noRun = IssueId.New();
        await SeedAsync(StartingRun.Begin(starting, WorkerRunId.New()));
        await SeedAsync(Active(active));
        await SeedAsync(Active(completed).Complete(exitCode: 0, branchName: null, pullRequestUrl: null));

        // Act
        IReadOnlySet<Guid> result = await QueryAsync(starting, active, completed, noRun);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task OmitsIssuesThatWereNotAskedFor()
    {
        // Arrange
        IssueId asked = IssueId.New();
        await SeedAsync(Publishing(asked));
        await SeedAsync(Publishing(IssueId.New()));

        // Act
        IReadOnlySet<Guid> result = await QueryAsync(asked);

        // Assert
        result.ShouldBe([asked.Value]);
    }
}
