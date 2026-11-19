using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

using static Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests.ReviewVerdictSamples;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.WorkerRunQueriesTests;

public sealed class GetWorkerRunDetailAsync_ReviewVerdict : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetWorkerRunDetailAsync_ReviewVerdict()
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

    [Fact]
    public async Task AReviewRunThatRecordedAVerdict_ShowsTheVerdictWithItsOutcome()
    {
        // Arrange
        SuppliedReviewFacts facts = Facts();
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        starting.RecordSuppliedReviewFacts(facts);
        ActiveRun active = starting.Activate(ContainerId.From("container-review"), branchName: null, MonitoredRepositoryId.New());
        RecordedReviewVerdict recorded = ReviewVerdict.TryParse(Verdict(criteria: [Criterion("AC1", "not_verifiable")]))
            .ShouldNotBeNull()
            .CheckAgainst(facts, PrincipleIds)
            .ShouldBeOfType<ReviewVerdictCheck.Accepted>()
            .Verdict
            .ToRecorded();
        CompletedRun completed = active.Complete(0, branchName: null, pullRequestUrl: null, reviewVerdict: recorded);
        await SeedAsync(completed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(completed.Id);

        // Assert
        RecordedReviewVerdict shown = detail.ReviewVerdict.ShouldNotBeNull();
        shown.ShouldSatisfyAllConditions(
            () => shown.ShouldBe(recorded),
            () => shown.Outcome.ShouldBe(ReviewVerdictOutcome.ApprovedForPerson));
    }

    [Fact]
    public async Task ARunWithoutAReviewVerdict_ShowsNone()
    {
        // Arrange
        FailedRun failed = PublishingRunFixture.ActiveRun().Fail(new FailureReason.NonZeroExit(1), branchNameOrNull: null);
        await SeedAsync(failed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(failed.Id);

        // Assert
        detail.ReviewVerdict.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AReviewRunWithSuppliedFacts_ShowsWhetherProductionChangedWithoutTests(bool productionWithoutTests)
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        starting.RecordSuppliedReviewFacts(Facts(productionWithoutTests: productionWithoutTests));
        await SeedAsync(starting);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(starting.Id);

        // Assert
        detail.ProductionChangedWithoutTests.ShouldBe(productionWithoutTests);
    }

    [Fact]
    public async Task ARunWithoutSuppliedReviewFacts_ShowsNoTestFlag()
    {
        // Arrange
        FailedRun failed = PublishingRunFixture.ActiveRun().Fail(new FailureReason.NonZeroExit(1), branchNameOrNull: null);
        await SeedAsync(failed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(failed.Id);

        // Assert
        detail.ProductionChangedWithoutTests.ShouldBeNull();
    }

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<WorkerRunDetail> GetDetailAsync(WorkerRunId id)
    {
        WorkerRunQueries sut = new(_dbContext);
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(id.Value, TestContext.Current.CancellationToken);
        return result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
    }
}
