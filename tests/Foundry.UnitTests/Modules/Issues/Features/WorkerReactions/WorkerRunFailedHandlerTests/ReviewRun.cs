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

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunFailedHandlerTests;

/// <summary>A failed review run never merges (ADR 0113): it retries while it can, then fails open to a person.</summary>
public sealed class ReviewRun : IAsyncDisposable
{
    private const string ReviewedCommit = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public ReviewRun()
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

    private WorkerRunFailedHandler BuildSut(int consecutiveTransientRuns) =>
        new(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            new NullWorkerRunQueries(consecutiveTransientRuns),
            NullLogger<WorkerRunFailedHandler>.Instance);

    private ReviewInProgressIssue SeedReviewInProgress(WorkerRunId workerRunId)
    {
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithWorkerRunId(workerRunId)
            .WithVerifiedHeadSha(ReviewedCommit)
            .WithAutoMerge(true)
            .ReviewInProgress();
        _dbContext.Set<Issue>().Add(reviewing);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return reviewing;
    }

    private async Task<Issue?> ReloadAsync(ReviewInProgressIssue reviewing)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>().FindAsync([reviewing.Id], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenTransientWithARetryLeft_TheIssueWaitsInReviewFailed()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId);
        WorkerRunFailed @event = new(workerRunId, reviewing.Id.Value, "API overloaded", FailureCategory.TransientApiErrorToken);

        // Act
        await BuildSut(consecutiveTransientRuns: 1).HandleAsync(@event, CancellationToken.None);

        // Assert
        ReviewFailedIssue failed = (await ReloadAsync(reviewing)).ShouldBeOfType<ReviewFailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(workerRunId),
            () => failed.FailureReason.ShouldBe("API overloaded"),
            () => failed.FailureCategory.ShouldBe(FailureCategory.TransientApiError),
            () => failed.ReviewedCommit.ShouldBe(ReviewedCommit),
            () => failed.AutoMerge.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenTransientWithRetriesExhausted_TheReviewIsSkippedToAPerson()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId);
        int maxRetries = FailureCategory.TransientApiError.AutomaticRetry!.MaxRetries;
        WorkerRunFailed @event = new(workerRunId, reviewing.Id.Value, "API overloaded", FailureCategory.TransientApiErrorToken);

        // Act
        await BuildSut(consecutiveTransientRuns: maxRetries).HandleAsync(@event, CancellationToken.None);

        // Assert
        ReviewIssue review = (await ReloadAsync(reviewing)).ShouldBeOfType<ReviewIssue>();
        review.ReviewOutcome.ShouldBe(new ReviewOutcome.Skipped(FailureCategory.TransientApiErrorToken));
    }

    [Fact]
    public async Task WhenNotTransient_TheReviewIsSkippedToAPersonWithTheCategoryAsReason()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId);
        WorkerRunFailed @event = new(workerRunId, reviewing.Id.Value, "Verdict missing", FailureCategory.VerdictInvalidToken);

        // Act
        await BuildSut(consecutiveTransientRuns: 0).HandleAsync(@event, CancellationToken.None);

        // Assert
        ReviewIssue review = (await ReloadAsync(reviewing)).ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.ReviewOutcome.ShouldBe(new ReviewOutcome.Skipped(FailureCategory.VerdictInvalidToken)),
            () => review.VerifiedHeadSha.ShouldBe(ReviewedCommit),
            () => review.MergeOutcome.ShouldBeNull());
    }

    [Fact]
    public async Task WhenTheRunIdIsStale_TheIssueStaysInReview()
    {
        // Arrange
        ReviewInProgressIssue reviewing = SeedReviewInProgress(WorkerRunId.New());
        WorkerRunFailed @event = new(WorkerRunId.New(), reviewing.Id.Value, "Verdict missing", FailureCategory.VerdictInvalidToken);

        // Act
        await BuildSut(consecutiveTransientRuns: 0).HandleAsync(@event, CancellationToken.None);

        // Assert
        (await ReloadAsync(reviewing)).ShouldBeOfType<ReviewInProgressIssue>();
    }
}
