using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewFactsTests;
using Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictParserTests;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ReviewOutcomeResolverTests;

public sealed class Resolve
{
    private static readonly ReviewFacts Facts = ReviewFactsBuilder.With(
        [new ChangedPath(ChangeStatus.Modified, "src/App.cs"), new ChangedPath(ChangeStatus.Added, "tests/AppTests.cs")],
        AcceptanceCriteria.Extract("## Acceptance Criteria\n\n- retries\n"));

    private static ReviewOutcomeResolver CreateSut(StubWorkerRoleCatalogStore? catalogStore = null) =>
        new(new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance), catalogStore ?? StubWorkerRoleCatalogStore.With());

    private static ActiveRun CreateReviewRun(ReviewFacts? facts)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);

        if (facts is not null)
        {
            starting.RecordSuppliedReviewFacts(facts);
        }

        return starting.Activate(ContainerId.From("container-review"), branchName: null, MonitoredRepositoryId.New());
    }

    private static string Output(JsonObject? structuredOutput, bool isError = false)
    {
        JsonObject result = new()
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = isError,
            ["result"] = "Done",
        };

        if (structuredOutput is not null)
        {
            result["structured_output"] = structuredOutput;
        }

        return result.ToJsonString();
    }

    private static WorkerStatus Exited(int exitCode) =>
        new(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void ResolvesRunsOfTheReviewRole()
    {
        // Assert
        CreateSut().Role.ShouldBe(WorkerRole.Review);
    }

    [Fact]
    public void WhenTheVerdictPassesItsChecks_CompletesWithTheDecidedOutcomeCommitAndPatchId()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(
            CreateReviewRun(Facts),
            Exited(0),
            Output(ReviewVerdictSamples.Valid("AC1").WithCriterion("AC1", "not_verifiable")));

        // Assert
        ReviewDecision decision = outcome.ShouldBeOfType<WorkerOutcome.ReviewCompleted>().Decision;
        decision.ShouldSatisfyAllConditions(
            () => decision.Outcome.ShouldBe(ReviewVerdictOutcome.ApprovedForPerson),
            () => decision.ReviewedCommit.ShouldBe(Facts.ReviewedCommit),
            () => decision.PatchId.ShouldBe(Facts.PatchId));
    }

    [Fact]
    public void WhenTheVerdictCitesAPrincipleTheCatalogCarries_Completes()
    {
        // Arrange
        JsonObject verdict = ReviewVerdictSamples.Valid("AC1")
            .WithFinding(ReviewVerdictSamples.Finding("design", "blocking", new JsonObject { ["principle"] = "design.1" }));
        StubWorkerRoleCatalogStore catalog = StubWorkerRoleCatalogStore.With(principles: new(
            "Principles.",
            new Dictionary<PrincipleSection, string>
            {
                [PrincipleSection.Testing] = "Testing:\n- one",
                [PrincipleSection.Design] = "Design:\n- one",
                [PrincipleSection.Code] = "Code:\n- one",
                [PrincipleSection.DomainModelling] = "Domain:\n- one",
            }));

        // Act
        WorkerOutcome outcome = CreateSut(catalog).Resolve(CreateReviewRun(Facts), Exited(0), Output(verdict));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.ReviewCompleted>().Decision.Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenNoCatalogIsLoaded_APrincipleCitationFailsClosed()
    {
        // Arrange
        JsonObject verdict = ReviewVerdictSamples.Valid("AC1")
            .WithFinding(ReviewVerdictSamples.Finding("design", "blocking", new JsonObject { ["principle"] = "design.1" }));

        // Act
        WorkerOutcome outcome = CreateSut(StubWorkerRoleCatalogStore.Empty()).Resolve(CreateReviewRun(Facts), Exited(0), Output(verdict));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>();
    }

    [Fact]
    public void WhenTheVerdictFailsItsChecks_FailsAsVerdictInvalidAndRecordsTheVerdictInvalid()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(Facts), Exited(0), Output(ReviewVerdictSamples.Valid("AC7")));

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>().Detail.ShouldContain("AC7");
        failure.Verdict.ShouldBe(RunVerdict.Invalid());
    }

    [Fact]
    public void WhenTheRunRecordedNoFacts_FailsAsVerdictInvalid()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(facts: null), Exited(0), Output(ReviewVerdictSamples.Valid("AC1")));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>()
            .Detail.ShouldBe(ReviewOutcomeResolver.NoSuppliedFactsDetail);
    }

    [Fact]
    public void WhenTheVerdictIsMissing_FailsAsVerdictInvalid()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(Facts), Exited(0), Output(structuredOutput: null));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>()
            .Detail.ShouldBe(FailureReason.VerdictInvalid.MissingDetail);
    }

    [Fact]
    public void WhenTheVerdictBreaksTheSchema_FailsAsVerdictInvalid()
    {
        // Arrange
        JsonObject verdict = ReviewVerdictSamples.Valid("AC1");
        verdict["outcome"] = "approved";

        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(Facts), Exited(0), Output(verdict));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>()
            .Detail.ShouldBe(FailureReason.VerdictInvalid.InvalidDetail);
    }

    [Fact]
    public void WhenAReviewRunExitsNonZero_ClassifiesTheExitAsForEveryRun()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(Facts), Exited(3), Output(ReviewVerdictSamples.Valid("AC1")));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.NonZeroExit>()
            .ExitCode.ShouldBe(3);
    }

    [Fact]
    public void WhenTheResultIsAnError_FailsRatherThanReadingTheVerdict()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(
            CreateReviewRun(Facts),
            Exited(0),
            Output(ReviewVerdictSamples.Valid("AC1"), isError: true));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }
}
