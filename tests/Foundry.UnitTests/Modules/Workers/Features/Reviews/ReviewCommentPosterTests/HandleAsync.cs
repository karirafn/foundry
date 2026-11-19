using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Reviews;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using static Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests.ReviewVerdictSamples;

namespace Foundry.UnitTests.Modules.Workers.Features.Reviews.ReviewCommentPosterTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string PullRequest = "https://github.com/acme/app/pull/7";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
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
    public async Task ACompletedReview_PostsItsVerdictAsOneCommentOnItsPullRequest()
    {
        // Arrange
        CompletedRun run = await SeedCompletedReviewAsync();
        RecordingRunPublishingCommands publishing = new();

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        (string pullRequestUrl, string body) = publishing.Summaries.ShouldHaveSingleItem();
        pullRequestUrl.ShouldSatisfyAllConditions(
            () => pullRequestUrl.ShouldBe(PullRequest),
            () => body.ShouldBe(ReviewVerdictComment.Compose(run.ReviewVerdict!)));
    }

    [Fact]
    public async Task APostedComment_LeavesNoFailureOnTheRun()
    {
        // Arrange
        CompletedRun run = await SeedCompletedReviewAsync();

        // Act
        await Sut(new RecordingRunPublishingCommands()).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        (await LoadRunAsync()).ReviewCommentFailure.ShouldBeNull();
    }

    [Theory]
    [InlineData(RunPublishingErrors.TransientCode)]
    [InlineData(RunPublishingErrors.PermanentCode)]
    public async Task AFailedPost_IsRecordedOnTheRunWithoutThrowing(string code)
    {
        // Arrange
        CompletedRun run = await SeedCompletedReviewAsync();
        RecordingRunPublishingCommands publishing = new() { PostError = new Foundry.Shared.Error(code, "The pull request is closed.") };

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        (await LoadRunAsync()).ReviewCommentFailure.ShouldNotBeNull().ShouldContain("The pull request is closed.");
    }

    [Fact]
    public async Task ARunThatIsNotACompletedReview_PostsNothing()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        _dbContext.Set<WorkerRun>().Add(starting);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        RecordingRunPublishingCommands publishing = new();

        // Act
        await Sut(publishing).HandleAsync(Completed(starting), TestContext.Current.CancellationToken);

        // Assert
        publishing.Summaries.ShouldBeEmpty();
    }

    private ReviewCommentPoster Sut(RecordingRunPublishingCommands publishing) =>
        new(_dbContext, publishing, NullLogger<ReviewCommentPoster>.Instance);

    private async Task<CompletedRun> SeedCompletedReviewAsync()
    {
        SuppliedReviewFacts facts = Facts();
        JsonObject json = Verdict();
        ReviewVerdict.Valid valid = ReviewVerdict.TryParse(json).ShouldNotBeNull()
            .CheckAgainst(facts, PrincipleIds)
            .ShouldBeOfType<ReviewVerdictCheck.Accepted>()
            .Verdict;
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        starting.RecordSuppliedReviewFacts(facts);
        starting.RecordPreparedBase(new RunBase(facts.ReviewedCommit, null), MonitoredRepositoryId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-review"), branchName: null, MonitoredRepositoryId.New());
        CompletedRun completed = active.Complete(0, null, null, reviewVerdict: valid.ToRecorded());
        _dbContext.Set<WorkerRun>().Add(completed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return completed;
    }

    private async Task<CompletedRun> LoadRunAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken)).ShouldBeOfType<CompletedRun>();
    }

    private static ReviewRunCompleted Completed(WorkerRun run) =>
        new(
            run.IssueId.Value,
            run.Id,
            ReviewVerdictOutcome.Approved,
            "0123456789abcdef0123456789abcdef01234567",
            PatchId: null,
            [],
            [],
            ReviewTestsAssessment.Adequate,
            DateTimeOffset.UtcNow);
}
