using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.ReviewRunCompletedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string ReviewedCommit = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ReviewRunCompletedHandler _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new ReviewRunCompletedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            TimeProvider.System,
            NullLogger<ReviewRunCompletedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ReviewInProgressIssue SeedReviewInProgress(WorkerRunId workerRunId, bool autoMerge)
    {
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithWorkerRunId(workerRunId)
            .WithVerifiedHeadSha(ReviewedCommit)
            .WithAutoMerge(autoMerge)
            .ReviewInProgress();
        _dbContext.Set<Issue>().Add(reviewing);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return reviewing;
    }

    private static ReviewRunCompleted Completed(Issue issue, WorkerRunId workerRunId, ReviewVerdictOutcome outcome) =>
        new(
            issue.Id.Value,
            workerRunId,
            outcome,
            ReviewedCommit,
            PatchId: "9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a2f1e0d",
            BlockingFindings: [],
            UnmetCriteria: [],
            ReviewTestsAssessment.Adequate,
            DateTimeOffset.UtcNow);

    private async Task<Issue?> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenApprovedWithAutoMerge_TheIssueMergesPinnedToTheReviewedCommit()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId, autoMerge: true);

        // Act
        await _sut.HandleAsync(
            Completed(reviewing, workerRunId, ReviewVerdictOutcome.Approved),
            TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(reviewing)).ShouldBeOfType<MergePendingIssue>().HeadSha.ShouldBe(ReviewedCommit);
    }

    [Fact]
    public async Task WhenApprovedForPerson_TheIssueGoesToReviewWithNoSkippedOutcome()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId, autoMerge: true);

        // Act
        await _sut.HandleAsync(
            Completed(reviewing, workerRunId, ReviewVerdictOutcome.ApprovedForPerson),
            TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue review = (await ReloadAsync(reviewing)).ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.ReviewOutcome.ShouldBeNull(),
            () => review.VerifiedHeadSha.ShouldBe(ReviewedCommit));
    }

    [Fact]
    public async Task WhenTheRunIdDoesNotMatch_TheIssueStaysInReview()
    {
        // Arrange
        ReviewInProgressIssue reviewing = SeedReviewInProgress(WorkerRunId.New(), autoMerge: true);

        // Act
        await _sut.HandleAsync(
            Completed(reviewing, WorkerRunId.New(), ReviewVerdictOutcome.Approved),
            TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(reviewing)).ShouldBeOfType<ReviewInProgressIssue>();
    }

    [Fact]
    public async Task WhenRedeliveredAfterTheIssueMovedOn_TheIssueIsLeftAsItIs()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId, autoMerge: false);
        ReviewRunCompleted @event = Completed(reviewing, workerRunId, ReviewVerdictOutcome.Approved);
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(reviewing)).ShouldBeOfType<ReviewIssue>();
    }
}
