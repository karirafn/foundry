using Foundry.Modules.Issues.Contracts;
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
    public async Task WhenApprovedForPerson_TheIssueRebasesBeforeReadyWithNoSkippedOutcome()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId, autoMerge: true);

        // Act
        await _sut.HandleAsync(
            Completed(reviewing, workerRunId, ReviewVerdictOutcome.ApprovedForPerson),
            TestContext.Current.CancellationToken);

        // Assert
        RebasePendingIssue rebasePending = (await ReloadAsync(reviewing)).ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ReviewOutcome.ShouldBeNull(),
            () => rebasePending.VerifiedHeadSha.ShouldBe(ReviewedCommit));
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
        (await ReloadAsync(reviewing)).ShouldBeOfType<RebasePendingIssue>();
    }

    [Fact]
    public async Task WhenChangesRequested_TheIssueQueuesAFindingsFixCarryingTheReviewsFindings()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId, autoMerge: true);
        ReviewFinding blocking = new("src/a.cs", 3, ReviewSeverity.Blocking, ReviewCategory.Correctness, null, "Off by one.");
        ReviewFinding advisory = new("src/b.cs", null, ReviewSeverity.Advisory, ReviewCategory.Quality, null, "Rename.");
        ReviewUnmetCriterion unmet = new("AC2", "Retries on 5xx.", "No retry path.");
        ReviewRunCompleted @event = Completed(reviewing, workerRunId, ReviewVerdictOutcome.ChangesRequested) with
        {
            BlockingFindings = [blocking],
            UnmetCriteria = ["AC2"],
            TestsAssessment = ReviewTestsAssessment.Missing,
            AdvisoryFindings = [advisory],
            UnmetCriterionDetails = [unmet],
            TestsReason = "No test covers the retry.",
        };

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        RevisionQueuedIssue queued = (await ReloadAsync(reviewing)).ShouldBeOfType<RevisionQueuedIssue>();
        RevisionTrigger.ReviewFindings findings = queued.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFindings>();
        queued.ShouldSatisfyAllConditions(
            () => queued.ReviewFixAttempts.ShouldBe(1),
            () => queued.HasPersonFeedback.ShouldBeFalse(),
            () => findings.Findings.ShouldBe([blocking, advisory]),
            () => findings.UnmetCriteria.ShouldBe([unmet]),
            () => findings.TestsAssessment.ShouldBe(ReviewTestsAssessment.Missing),
            () => findings.TestsReason.ShouldBe("No test covers the retry."));
    }

    [Fact]
    public async Task WhenChangesRequestedByAnEventWithoutDetails_TheFindingsFixKeepsTheUnmetCriterionIds()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId, autoMerge: false);
        ReviewRunCompleted @event = Completed(reviewing, workerRunId, ReviewVerdictOutcome.ChangesRequested) with
        {
            UnmetCriteria = ["AC1"],
        };

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        RevisionTrigger.ReviewFindings findings = (await ReloadAsync(reviewing))
            .ShouldBeOfType<RevisionQueuedIssue>()
            .Trigger.ShouldBeOfType<RevisionTrigger.ReviewFindings>();
        findings.ShouldSatisfyAllConditions(
            () => findings.UnmetCriteria.Select(criterion => criterion.Id).ShouldBe(["AC1"]),
            () => findings.Findings.ShouldBeEmpty(),
            () => findings.TestsReason.ShouldBeEmpty());
    }

    [Theory]
    [InlineData(ReviewVerdictOutcome.Approved)]
    [InlineData(ReviewVerdictOutcome.ApprovedForPerson)]
    [InlineData(ReviewVerdictOutcome.ChangesRequested)]
    public async Task WhenTheReviewCompletes_TheNextStateStoresItAsTheLatestReview(ReviewVerdictOutcome outcome)
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId, autoMerge: true);
        ReviewRunCompleted @event = Completed(reviewing, workerRunId, outcome);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        Issue next = (await ReloadAsync(reviewing)).ShouldNotBeNull();
        next.LatestReview.ShouldBe(new LatestReview(workerRunId, outcome, @event.PatchId));
    }

    [Fact]
    public async Task WhenTheReviewedDiffHadNoPatchId_TheLatestReviewStoresNone()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ReviewInProgressIssue reviewing = SeedReviewInProgress(workerRunId, autoMerge: false);
        ReviewRunCompleted @event = Completed(reviewing, workerRunId, ReviewVerdictOutcome.Approved) with { PatchId = null };

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(reviewing)).ShouldNotBeNull().LatestReview.ShouldNotBeNull().PatchId.ShouldBeNull();
    }
}
