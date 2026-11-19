using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

using static Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests.ReviewVerdictSamples;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeApplierTests;

public sealed partial class ApplyAsync
{
    private const string ReviewContainerId = "container-review";

    private void SeedActiveReviewRun(SuppliedReviewFacts facts)
    {
        using FoundryDbContext db = CreateDbContext();
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        starting.RecordSuppliedReviewFacts(facts);
        db.Set<WorkerRun>().Add(starting.Activate(Foundry.Modules.Workers.Domain.ValueObjects.ContainerId.From(ReviewContainerId), branchName: null, MonitoredRepositoryId.New()));
        db.SaveChanges();
    }

    private static WorkerOutcome.ReviewCompleted ReviewCompletedFor(JsonObject verdict, SuppliedReviewFacts facts) =>
        new(
            ReviewVerdict.TryParse(verdict).ShouldNotBeNull()
                .CheckAgainst(facts, PrincipleIds)
                .ShouldBeOfType<ReviewVerdictCheck.Accepted>()
                .Verdict,
            ContainerOutput: null,
            Summary: null);

    [Fact]
    public async Task WhenAReviewRunIsLoaded_ItsSuppliedReviewFactsAreTheOnesRecordedAtDispatch()
    {
        // Arrange
        SuppliedReviewFacts facts = Facts(criteriaCount: 2, productionWithoutTests: true);
        SeedActiveReviewRun(facts);

        // Act
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        activeRun.SuppliedReviewFacts.ShouldSatisfyAllConditions(
            loaded => loaded.ShouldBe(facts),
            loaded => loaded!.ProductionChangedWithoutTests.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenAReviewCompletes_CompletesTheRunWithoutABranchAndStoresTheVerdictWithItsOutcome()
    {
        // Arrange
        SuppliedReviewFacts facts = Facts();
        SeedActiveReviewRun(facts);
        RecordingOrchestrator orchestrator = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, orchestrator);
        WorkerOutcome.ReviewCompleted outcome = ReviewCompletedFor(
            Verdict(findings: [Finding("correctness")], dimensionStatuses: new() { ["correctness"] = "findings" }),
            facts);

        // Act
        await sut.ApplyAsync(outcome, activeRun, RunVerdict.Done(), TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        CompletedRun run = (await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<CompletedRun>();
        RecordedReviewVerdict stored = run.ReviewVerdict.ShouldNotBeNull();
        run.ShouldSatisfyAllConditions(
            () => run.BranchName.ShouldBeNull(),
            () => run.PullRequestUrl.ShouldBeNull(),
            () => run.VerdictStatus.ShouldBe(RunVerdictStatus.Done),
            () => stored.ShouldBe(outcome.Verdict.ToRecorded()),
            () => stored.Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested),
            () => stored.ReviewedCommit.ShouldBe(facts.ReviewedCommit.Value),
            () => stored.PatchId.ShouldBe(facts.PatchId!.Value.Value),
            () => run.SuppliedReviewFacts.ShouldBe(facts),
            () => orchestrator.StopAndRemoveCalls.ShouldBe([ReviewContainerId]));
    }

    [Fact]
    public async Task WhenAReviewCompletes_PublishesReviewRunCompletedInsteadOfWorkerRunCompleted()
    {
        // Arrange
        SuppliedReviewFacts facts = Facts(criteriaCount: 2);
        SeedActiveReviewRun(facts);
        CapturingIntegrationEventDispatcher dispatcher = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator(), dispatcher);
        WorkerOutcome.ReviewCompleted outcome = ReviewCompletedFor(
            Verdict(
                findings: [Finding("security"), Finding("correctness", severity: "advisory")],
                dimensionStatuses: new() { ["security"] = "findings" },
                criteria: [Criterion("AC1", "unmet"), Criterion("AC2", "met")],
                assessment: "not_needed"),
            facts);

        // Act
        await sut.ApplyAsync(outcome, activeRun, TestContext.Current.CancellationToken);

        // Assert
        ReviewRunCompleted completed = dispatcher.Captured.OfType<ReviewRunCompleted>().ShouldHaveSingleItem();
        completed.ShouldSatisfyAllConditions(
            () => completed.IssueId.ShouldBe(activeRun.IssueId.Value),
            () => completed.WorkerRunId.ShouldBe(activeRun.Id),
            () => completed.Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested),
            () => completed.ReviewedCommit.ShouldBe(facts.ReviewedCommit.Value),
            () => completed.PatchId.ShouldBe(facts.PatchId!.Value.Value),
            () => completed.BlockingFindings.ShouldHaveSingleItem().Category.ShouldBe(ReviewCategory.Security),
            () => completed.UnmetCriteria.ShouldBe(["AC1"]),
            () => completed.TestsAssessment.ShouldBe(ReviewTestsAssessment.NotNeeded),
            () => dispatcher.Captured.OfType<WorkerRunCompleted>().ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenAReviewCompletes_ReviewRunCompletedCarriesAdvisoryFindingsUnmetCriterionTextWithEvidenceAndTheTestsReason()
    {
        // Arrange
        SuppliedReviewFacts facts = Facts(criteriaCount: 2);
        SeedActiveReviewRun(facts);
        CapturingIntegrationEventDispatcher dispatcher = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator(), dispatcher);
        WorkerOutcome.ReviewCompleted outcome = ReviewCompletedFor(
            Verdict(
                findings: [Finding("security"), Finding("correctness", severity: "advisory")],
                dimensionStatuses: new() { ["security"] = "findings" },
                criteria: [Criterion("AC1", "met"), Criterion("AC2", "unmet")]),
            facts);

        // Act
        await sut.ApplyAsync(outcome, activeRun, TestContext.Current.CancellationToken);

        // Assert
        ReviewRunCompleted completed = dispatcher.Captured.OfType<ReviewRunCompleted>().ShouldHaveSingleItem();
        completed.ShouldSatisfyAllConditions(
            () => completed.AdvisoryFindings.ShouldNotBeNull().ShouldHaveSingleItem().Category.ShouldBe(ReviewCategory.Correctness),
            () => completed.UnmetCriterionDetails.ShouldBe([new ReviewUnmetCriterion("AC2", "Criterion 2.", "See tests.")]),
            () => completed.TestsReason.ShouldBe("Covered."));
    }
}
