using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerRunStarterTests;

public sealed partial class HandleAsync
{
    private const string EarlierReviewedCommit = "1111111111111111111111111111111111111111";
    private const string OtherPatchId = "fedcbafedcbafedcbafedcbafedcbafedcbafedc";

    private static readonly ReviewFinding AdvisoryFinding = new(
        "src/Retry.cs",
        12,
        ReviewSeverity.Advisory,
        ReviewCategory.Quality,
        Rule: null,
        "Consider naming the retry count.");

    private static RecordedReviewVerdict EarlierVerdict(
        ReviewVerdictOutcome outcome = ReviewVerdictOutcome.Approved,
        string? patchId = ReviewPatchId) =>
        new(
            "Retries are bounded and tested.",
            [AdvisoryFinding],
            [],
            [new ReviewCriterionAnswer("AC1", ReviewCriterionStatus.Met, "RetryTests covers it.")],
            new ReviewTests(ReviewTestsAssessment.Adequate, "RetryTests covers the retry.", ["tests/RetryTests.cs"]),
            outcome,
            EarlierReviewedCommit,
            patchId);

    private static DispatchContext.Review CarryingContext(string? carriedPatchId = ReviewPatchId) =>
        ReviewContext() with { CarriedPatchId = carriedPatchId };

    private async Task<WorkerRunId> SeedEarlierReviewAsync(IssueId issueId, RecordedReviewVerdict verdict)
    {
        WorkerRunId runId = WorkerRunId.New();
        ActiveRun active = StartingRun.Begin(issueId, runId, WorkerRole.Review)
            .Activate(ContainerId.From("earlier-review"), branchName: null, MonitoredRepositoryId.New());
        _dbContext.Set<WorkerRun>().Add(active.Complete(0, branchName: null, pullRequestUrl: null, reviewVerdict: verdict));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return runId;
    }

    private async Task<WorkerRun> GetRunAsync(WorkerRunId runId)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<WorkerRun>().SingleAsync(run => run.Id == runId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenTheChangeIsUnchangedSinceTheLatestApproval_TheRunCompletesAsCarriedWithoutAWorker()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        WorkerRunId earlierRunId = await SeedEarlierReviewAsync(issueId, EarlierVerdict());
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        ClaimedRunPipeline sut = BuildReviewHandler(orchestrator: orchestrator);
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(issueId: issueId, workerRunId: runId, context: CarryingContext(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        CompletedRun run = (await GetRunAsync(runId)).ShouldBeOfType<CompletedRun>();
        RecordedReviewVerdict verdict = run.ReviewVerdict.ShouldNotBeNull();
        verdict.ShouldSatisfyAllConditions(
            () => orchestrator.LastSpec.ShouldBeNull(),
            () => verdict.CarriedFrom.ShouldBe(new ReviewCarry(earlierRunId, EarlierReviewedCommit)),
            () => verdict.ReviewedCommit.ShouldBe(ReviewedCommitSha),
            () => verdict.Outcome.ShouldBe(ReviewVerdictOutcome.Approved),
            () => verdict.Summary.ShouldBe("Retries are bounded and tested."),
            () => verdict.Findings.ShouldBe([AdvisoryFinding]),
            () => verdict.PatchId.ShouldBe(ReviewPatchId),
            () => run.SuppliedReviewFacts.ShouldNotBeNull().ReviewedCommit.ShouldBe(CommitSha.From(ReviewedCommitSha)));
    }

    [Fact]
    public async Task WhenTheApprovalCarries_ThePullRequestDescriptionIsNotRead()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedEarlierReviewAsync(issueId, EarlierVerdict());
        StubIssueContentQueries contentQueries = ReviewContentQueries();
        ClaimedRunPipeline sut = BuildReviewHandler(issueContentQueries: contentQueries);

        // Act
        await sut.HandleAsync(
            BuildEvent(issueId: issueId, context: CarryingContext(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        contentQueries.PullRequestRequests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ReviewVerdictOutcome.Approved)]
    [InlineData(ReviewVerdictOutcome.ApprovedForPerson)]
    public async Task WhenTheApprovalCarries_ItRaisesReviewRunCompletedWithTheEarlierOutcomeAndTheNewCommit(
        ReviewVerdictOutcome outcome)
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedEarlierReviewAsync(issueId, EarlierVerdict(outcome));
        ClaimedRunPipeline sut = BuildReviewHandler();
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(issueId: issueId, workerRunId: runId, context: CarryingContext(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        ReviewRunCompleted completed = _integrationEvents.DispatchedEvents.OfType<ReviewRunCompleted>().ShouldHaveSingleItem();
        completed.ShouldSatisfyAllConditions(
            () => completed.IssueId.ShouldBe(issueId.Value),
            () => completed.WorkerRunId.ShouldBe(runId),
            () => completed.Outcome.ShouldBe(outcome),
            () => completed.ReviewedCommit.ShouldBe(ReviewedCommitSha),
            () => completed.PatchId.ShouldBe(ReviewPatchId),
            () => completed.BlockingFindings.ShouldBeEmpty(),
            () => completed.AdvisoryFindings.ShouldBe([AdvisoryFinding]),
            () => completed.UnmetCriteria.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTheLatestApprovalWasItselfCarried_TheCarriedVerdictStillNamesTheReviewThatJudgedTheChange()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        WorkerRunId originalRunId = WorkerRunId.New();
        RecordedReviewVerdict carriedOnce = EarlierVerdict().CarryTo("2222222222222222222222222222222222222222", originalRunId);
        await SeedEarlierReviewAsync(issueId, carriedOnce);
        ClaimedRunPipeline sut = BuildReviewHandler();
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(issueId: issueId, workerRunId: runId, context: CarryingContext(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        (await GetRunAsync(runId)).ReviewVerdict.ShouldNotBeNull().CarriedFrom
            .ShouldBe(new ReviewCarry(originalRunId, EarlierReviewedCommit));
    }

    [Fact]
    public async Task WhenThePreparedPatchIdDiffers_TheReviewRunsInFull()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedEarlierReviewAsync(issueId, EarlierVerdict());
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        ClaimedRunPipeline sut = BuildReviewHandler(orchestrator: orchestrator);
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(issueId: issueId, workerRunId: runId, context: CarryingContext(OtherPatchId), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        orchestrator.LastSpec.ShouldNotBeNull();
        (await GetRunAsync(runId)).ShouldBeOfType<ActiveRun>().ReviewVerdict.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheContextCarriesNoPatchId_TheReviewRunsInFull()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedEarlierReviewAsync(issueId, EarlierVerdict());
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        ClaimedRunPipeline sut = BuildReviewHandler(orchestrator: orchestrator);
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(issueId: issueId, workerRunId: runId, context: CarryingContext(carriedPatchId: null), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        orchestrator.LastSpec.ShouldNotBeNull();
        (await GetRunAsync(runId)).ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenThePreparedChangeHasNoPatchId_TheReviewRunsInFull()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedEarlierReviewAsync(issueId, EarlierVerdict());
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        ClaimedRunPipeline sut = BuildReviewHandler(
            orchestrator: orchestrator,
            publisher: new FakePublisher(reviewPacket: new ReviewPacketFacts(ReviewPacket.ChangedPaths, patchId: null)));
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(issueId: issueId, workerRunId: runId, context: CarryingContext(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        orchestrator.LastSpec.ShouldNotBeNull();
        (await GetRunAsync(runId)).ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenTheLatestReviewRequestedChanges_TheReviewRunsInFull()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedEarlierReviewAsync(issueId, EarlierVerdict(ReviewVerdictOutcome.ChangesRequested));
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        ClaimedRunPipeline sut = BuildReviewHandler(orchestrator: orchestrator);
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(issueId: issueId, workerRunId: runId, context: CarryingContext(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        orchestrator.LastSpec.ShouldNotBeNull();
        _integrationEvents.DispatchedEvents.OfType<ReviewRunCompleted>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoEarlierReviewIsRecorded_TheReviewRunsInFull()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        ClaimedRunPipeline sut = BuildReviewHandler(orchestrator: orchestrator);
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(workerRunId: runId, context: CarryingContext(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        orchestrator.LastSpec.ShouldNotBeNull();
        (await GetRunAsync(runId)).ShouldBeOfType<ActiveRun>();
    }
}
