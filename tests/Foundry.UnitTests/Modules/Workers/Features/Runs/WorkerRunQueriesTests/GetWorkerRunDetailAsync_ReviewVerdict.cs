using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;
using Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewFactsTests;
using Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictParserTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

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
    public async Task ACompletedReviewRun_ShowsItsStoredVerdictAndOutcome()
    {
        // Arrange
        ReviewFacts facts = ReviewFactsBuilder.With([new ChangedPath(ChangeStatus.Modified, "src/App.cs")]);
        ReviewVerdict verdict = ReviewVerdictParser.TryParse(ReviewVerdictSamples.Valid()
            .WithFinding(ReviewVerdictSamples.Finding("security", "advisory"))
            .WithTests("not_needed")).ShouldNotBeNull();
        ReviewDecision decision = ReviewDecision.Decide(verdict, facts, []).ShouldBeOfType<ReviewDecisionResult.Decided>().Decision;
        CompletedRun completed = PublishingRunFixture.ActiveRun().CompleteReview(decision);
        await SeedAsync(completed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(completed.Id);

        // Assert
        WorkerRunReviewVerdict shown = detail.ReviewVerdict.ShouldNotBeNull();
        shown.ShouldSatisfyAllConditions(
            () => shown.Outcome.ShouldBe(ReviewVerdictOutcome.ApprovedForPerson),
            () => shown.ReviewedCommit.ShouldBe(facts.ReviewedCommit.Value),
            () => shown.PatchId.ShouldBe(facts.PatchId!.Value.Value),
            () => shown.Summary.ShouldBe(verdict.Summary),
            () => shown.Findings.ShouldBe(verdict.Findings),
            () => shown.Dimensions.Count.ShouldBe(7),
            () => shown.Criteria.ShouldBeEmpty(),
            () => shown.Tests.Assessment.ShouldBe(ReviewTestsAssessment.NotNeeded));
    }

    [Fact]
    public async Task ARunThatIsNotAReview_ShowsNoReviewVerdict()
    {
        // Arrange
        CompletedRun completed = PublishingRunFixture.ActiveRun().Complete(0, branchName: null, pullRequestUrl: null);
        await SeedAsync(completed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(completed.Id);

        // Assert
        detail.ReviewVerdict.ShouldBeNull();
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
