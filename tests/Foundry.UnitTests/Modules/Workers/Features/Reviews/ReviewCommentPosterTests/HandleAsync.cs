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

    [Fact]
    public async Task AFindingOnAnAddedLine_IsPostedInlineBeforeTheSummaryComment()
    {
        // Arrange
        CompletedRun run = await SeedCompletedReviewAsync(
            VerdictWith(FindingAt(3, "Retries forever.")),
            Facts(addedLines: [new AddedLineRange("src/a.cs", 2, 4)]));
        RecordingRunPublishingCommands publishing = new();

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        (string pullRequestUrl, string commitSha, string summary, IReadOnlyList<InlineReviewComment> comments) =
            publishing.InlineReviews.ShouldHaveSingleItem();
        publishing.ShouldSatisfyAllConditions(
            () => publishing.Calls.ShouldBe(["inline", "summary"]),
            () => pullRequestUrl.ShouldBe(PullRequest),
            () => commitSha.ShouldBe(run.SuppliedReviewFacts!.ReviewedCommit.Value),
            () => summary.ShouldStartWith("1 finding from the review of"),
            () => comments.ShouldBe([new InlineReviewComment("src/a.cs", 3, "Retries forever. (correctness)")]));
    }

    [Fact]
    public async Task AFindingPostedInline_IsLeftOutOfTheSummaryCommentsLists()
    {
        // Arrange
        CompletedRun run = await SeedCompletedReviewAsync(
            VerdictWith(FindingAt(3, "Retries forever."), FindingAt(9, "Logs the token.")),
            Facts(addedLines: [new AddedLineRange("src/a.cs", 3, 3)]));
        RecordingRunPublishingCommands publishing = new();

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        string body = publishing.Summaries.ShouldHaveSingleItem().Body;
        body.ShouldSatisfyAllConditions(
            () => body.ShouldContain("_1 finding is posted inline on the changed lines._"),
            () => body.ShouldContain("Logs the token."),
            () => body.ShouldNotContain("Retries forever."),
            () => publishing.InlineReviews.ShouldHaveSingleItem().Comments.ShouldHaveSingleItem().Line.ShouldBe(3));
    }

    [Fact]
    public async Task FindingsOffTheAddedLines_PostNoInlineReview()
    {
        // Arrange — line 9 is not added, and a finding without a line has no place in the diff
        JsonObject noLine = FindingAt(3, "Whole file.");
        noLine.Remove("line");
        CompletedRun run = await SeedCompletedReviewAsync(
            VerdictWith(FindingAt(9, "Logs the token."), noLine),
            Facts(addedLines: [new AddedLineRange("src/a.cs", 3, 3)]));
        RecordingRunPublishingCommands publishing = new();

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        publishing.ShouldSatisfyAllConditions(
            () => publishing.Calls.ShouldBe(["summary"]),
            () => publishing.Summaries.ShouldHaveSingleItem().Body.ShouldContain("Logs the token."));
    }

    [Fact]
    public async Task AFailedInlinePost_FallsBackToTheSummaryCommentAndIsRecordedOnTheRun()
    {
        // Arrange
        CompletedRun run = await SeedCompletedReviewAsync(
            VerdictWith(FindingAt(3, "Retries forever.")),
            Facts(addedLines: [new AddedLineRange("src/a.cs", 3, 3)]));
        RecordingRunPublishingCommands publishing = new()
        {
            InlineError = new Foundry.Shared.Error(RunPublishingErrors.PermanentCode, "Validation Failed."),
        };

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        string body = publishing.Summaries.ShouldHaveSingleItem().Body;
        CompletedRun saved = await LoadRunAsync();
        body.ShouldSatisfyAllConditions(
            () => body.ShouldContain("Retries forever."),
            () => body.ShouldNotContain("posted inline"),
            () => saved.ReviewCommentFailure.ShouldNotBeNull().ShouldContain("Validation Failed."));
    }

    [Fact]
    public async Task AnInlineCommentTheProviderRejects_FallsBackAloneAndIsRecordedOnTheRun()
    {
        // Arrange
        CompletedRun run = await SeedCompletedReviewAsync(
            VerdictWith(FindingAt(3, "Retries forever."), FindingAt(4, "Unbounded.")),
            Facts(addedLines: [new AddedLineRange("src/a.cs", 3, 4)]));
        RecordingRunPublishingCommands publishing = new() { RejectedLocations = new HashSet<string> { "src/a.cs:4" } };

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        string body = publishing.Summaries.ShouldHaveSingleItem().Body;
        CompletedRun saved = await LoadRunAsync();
        body.ShouldSatisfyAllConditions(
            () => body.ShouldContain("Unbounded."),
            () => body.ShouldNotContain("Retries forever."),
            () => saved.ReviewCommentFailure.ShouldNotBeNull().ShouldContain("src/a.cs:4"));
    }

    [Fact]
    public async Task AModelWrittenMentionInAnInlineFinding_IsNeutralized()
    {
        // Arrange
        CompletedRun run = await SeedCompletedReviewAsync(
            VerdictWith(FindingAt(3, "Ask @alice <b>now</b>.")),
            Facts(addedLines: [new AddedLineRange("src/a.cs", 3, 3)]));
        RecordingRunPublishingCommands publishing = new();

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        publishing.InlineReviews.ShouldHaveSingleItem().Comments.ShouldHaveSingleItem().Body
            .ShouldBe("Ask @\u200Balice &lt;b&gt;now&lt;/b&gt;. (correctness)");
    }

    [Fact]
    public async Task ACarriedApproval_PostsItsOneLineNoteAndNoInlineReview()
    {
        // Arrange — the carried approval's findings were posted by the review it carries
        JsonObject advisory = Finding("correctness", severity: "advisory");
        CompletedRun run = await SeedCompletedReviewAsync(
            Verdict(findings: [advisory], dimensionStatuses: new() { ["correctness"] = "findings" }),
            Facts(addedLines: [new AddedLineRange("src/a.cs", 3, 3)]),
            carry: true);
        RecordingRunPublishingCommands publishing = new();

        // Act
        await Sut(publishing).HandleAsync(Completed(run), TestContext.Current.CancellationToken);

        // Assert
        publishing.ShouldSatisfyAllConditions(
            () => publishing.Calls.ShouldBe(["summary"]),
            () => publishing.Summaries.ShouldHaveSingleItem().Body.ShouldStartWith("Approval carried to"));
    }

    private static JsonObject VerdictWith(params JsonObject[] findings) =>
        Verdict(findings: [.. findings], dimensionStatuses: new() { ["correctness"] = "findings" });

    private static JsonObject FindingAt(int line, string body)
    {
        JsonObject finding = Finding("correctness");
        finding["line"] = line;
        finding["body"] = body;
        return finding;
    }

    private ReviewCommentPoster Sut(RecordingRunPublishingCommands publishing) =>
        new(_dbContext, publishing, NullLogger<ReviewCommentPoster>.Instance);

    private async Task<CompletedRun> SeedCompletedReviewAsync(JsonObject? verdict = null, SuppliedReviewFacts? suppliedFacts = null, bool carry = false)
    {
        SuppliedReviewFacts facts = suppliedFacts ?? Facts();
        JsonObject json = verdict ?? Verdict();
        ReviewVerdict.Valid valid = ReviewVerdict.TryParse(json).ShouldNotBeNull()
            .CheckAgainst(facts, PrincipleIds)
            .ShouldBeOfType<ReviewVerdictCheck.Accepted>()
            .Verdict;
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        starting.RecordSuppliedReviewFacts(facts);
        starting.RecordPreparedBase(new RunBase(facts.ReviewedCommit, null), MonitoredRepositoryId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-review"), branchName: null, MonitoredRepositoryId.New());
        RecordedReviewVerdict recorded = valid.ToRecorded();
        if (carry)
        {
            recorded = recorded.CarryTo("fedcba9876543210fedcba9876543210fedcba98", WorkerRunId.New());
        }

        CompletedRun completed = active.Complete(0, null, null, reviewVerdict: recorded);
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
