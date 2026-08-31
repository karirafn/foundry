using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueSummaryAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly StubWorkerRunQueries _workerRunQueries;
    private readonly IIssueQueries _sut;

    public GetIssueSummaryAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _workerRunQueries = new StubWorkerRunQueries();
        _sut = new IssueQueries(
            _dbContext,
            new NullRepositorySlugQueries(),
            new NullRepositoryEligibilityQuery(),
            _workerRunQueries);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<DetectedIssue> CreateAndPersistDetectedIssueAsync(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .Detected();

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return issue;
    }

    [Fact]
    public async Task WhenIssueDoesNotExist_ReturnsNull()
    {
        // Arrange
        IssueId unknownId = IssueId.New();

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(unknownId, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenIssueHasNoWorkerRuns_RunStatsIsNull()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = await CreateAndPersistDetectedIssueAsync(repositoryId);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.RunStats.ShouldBeNull();
    }

    [Fact]
    public async Task WhenIssueHasWorkerRuns_RunStatsIsPopulated()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = await CreateAndPersistDetectedIssueAsync(repositoryId);

        RunAggregate aggregate = new(
            RunCount: 2,
            DurationMs: 3000L,
            NumTurns: 8,
            TotalCostUsd: 0.30m,
            InputTokens: 1300L,
            OutputTokens: 500L);

        _workerRunQueries.SetAggregate(issue.Id.Value, aggregate);

        // Act
        IssueSummary? result = await _sut.GetIssueSummaryAsync(issue.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        RunStats stats = result.RunStats.ShouldNotBeNull();
        stats.ShouldSatisfyAllConditions(
            () => stats.RunCount.ShouldBe(2),
            () => stats.DurationMs.ShouldBe(3000L),
            () => stats.NumTurns.ShouldBe(8),
            () => stats.TotalCostUsd.ShouldBe(0.30m),
            () => stats.InputTokens.ShouldBe(1300L),
            () => stats.OutputTokens.ShouldBe(500L));
    }

    private sealed class StubWorkerRunQueries : IWorkerRunQueries
    {
        private readonly Dictionary<Guid, RunAggregate> _aggregates = [];

        public void SetAggregate(Guid issueId, RunAggregate aggregate)
        {
            _aggregates[issueId] = aggregate;
        }

        public Task<IReadOnlyDictionary<Guid, RunAggregate>> GetRunAggregatesForIssuesAsync(
            IReadOnlyCollection<Guid> issueIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, RunAggregate>>(
                issueIds
                    .Where(_aggregates.ContainsKey)
                    .ToDictionary(id => id, id => _aggregates[id]));

        public Task<Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
            Guid workerRunId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<WorkerRunDetail>.Fail(new Error("Test.NotFound", "Not found")));

        public Task<WorkerRunLogResult> GetWorkerRunLogAsync(
            Guid workerRunId,
            CancellationToken cancellationToken)
            => Task.FromResult<WorkerRunLogResult>(new WorkerRunLogResult.RunNotFound());

        public Task<RunTotals> GetRunTotalsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
            => Task.FromResult(new RunTotals(0, 0L, 0, 0m, 0L, 0L));

        public Task<int> CountConsecutiveTransientRunsAsync(
            Guid issueId,
            int maxAttempts,
            CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyCollection<WorkerActivity>> GetActiveRunActivityAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<WorkerActivity>>([]);
    }
}
