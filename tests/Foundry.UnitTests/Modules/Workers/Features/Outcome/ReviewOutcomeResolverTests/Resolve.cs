using System.Text.Json.Nodes;

using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using static Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests.ReviewVerdictSamples;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ReviewOutcomeResolverTests;

public sealed class Resolve
{
    private static readonly WorkerPrinciples Principles = new(
        "Framing.",
        new Dictionary<PrincipleSection, string>
        {
            [PrincipleSection.Testing] = "Testing:\n- First.",
            [PrincipleSection.Design] = "Design:\n- First.\n- Second.",
            [PrincipleSection.Code] = "Code:\n- First.",
            [PrincipleSection.DomainModelling] = "Domain:\n- First.",
        });

    private static ReviewOutcomeResolver CreateSut(IWorkerRoleCatalogStore? store = null) =>
        new(
            new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance, new PlanUsageWindowReader()),
            store ?? StubWorkerRoleCatalogStore.With(principles: Principles));

    private static ActiveRun CreateReviewRun(bool withFacts = true)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);

        if (withFacts)
        {
            starting.RecordSuppliedReviewFacts(Facts());
        }

        return starting.Activate(ContainerId.From("container-review"), branchName: null, MonitoredRepositoryId.New());
    }

    private static string ResultLine(JsonNode? structuredOutput, bool isError = false)
    {
        JsonObject line = new()
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = isError,
            ["result"] = "Done",
        };

        if (structuredOutput is not null)
        {
            line["structured_output"] = structuredOutput;
        }

        return line.ToJsonString();
    }

    private static WorkerStatus Exited(int exitCode) =>
        new(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow);

    private static JsonObject DesignFindingCiting(string principle) =>
        Verdict(
            findings: [Finding("design", rule: new JsonObject { ["principle"] = principle })],
            dimensionStatuses: new() { ["design"] = "findings" });

    [Fact]
    public void ResolvesRunsOfTheReviewRole()
    {
        // Assert
        CreateSut().Role.ShouldBe(WorkerRole.Review);
    }

    [Fact]
    public void WhenAReviewRunExitsNonZero_ClassifiesTheExitAsForEveryRun()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(3), ResultLine(Verdict()));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.NonZeroExit>()
            .ExitCode.ShouldBe(3);
    }

    [Fact]
    public void WhenTheResultIsAnError_FailsWithoutReadingTheVerdict()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(0), ResultLine(Verdict(), isError: true));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason.ShouldNotBeOfType<FailureReason.VerdictInvalid>();
    }

    [Fact]
    public void WhenTheVerdictIsMissing_FailsAsVerdictInvalid()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(0), ResultLine(structuredOutput: null));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>()
            .Detail.ShouldBe(FailureReason.VerdictInvalid.MissingDetail);
    }

    [Fact]
    public void WhenTheVerdictBreaksTheSchema_FailsAsVerdictInvalid()
    {
        // Arrange
        JsonObject verdict = Verdict();
        verdict["outcome"] = "approved";

        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(0), ResultLine(verdict));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>()
            .Detail.ShouldBe(FailureReason.VerdictInvalid.InvalidDetail);
    }

    [Fact]
    public void WhenTheRunRecordedNoReviewFacts_FailsAsVerdictInvalidAndVoidsTheVerdict()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(withFacts: false), Exited(0), ResultLine(Verdict()));

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.ShouldSatisfyAllConditions(
            () => failure.FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>().Detail.ShouldBe(ReviewOutcomeResolver.NoSuppliedFactsDetail),
            () => failure.Verdict.ShouldBe(RunVerdict.Invalid()));
    }

    [Fact]
    public void WhenTheVerdictFailsACheckAgainstTheFacts_FailsAsVerdictInvalidAndVoidsTheVerdict()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(
            CreateReviewRun(),
            Exited(0),
            ResultLine(Verdict(criteria: [Criterion("AC2", "met")])));

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.ShouldSatisfyAllConditions(
            () => failure.FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>().Detail.ShouldContain("AC2"),
            () => failure.Verdict.ShouldBe(RunVerdict.Invalid()));
    }

    [Fact]
    public void WhenAFindingCitesAPrincipleOfTheRolesCatalog_CompletesTheReview()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(0), ResultLine(DesignFindingCiting("design.2")));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.ReviewCompleted>().Verdict.Outcome.ShouldBe(ReviewVerdictOutcome.ChangesRequested);
    }

    [Fact]
    public void WhenAFindingCitesAPrincipleOutsideTheRolesCatalog_FailsAsVerdictInvalid()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(0), ResultLine(DesignFindingCiting("design.3")));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>()
            .Detail.ShouldContain("'design.3'");
    }

    [Fact]
    public void WhenNoCatalogIsLoadedAndAFindingCitesAPrinciple_FailsAsVerdictInvalid()
    {
        // Act
        WorkerOutcome outcome = CreateSut(StubWorkerRoleCatalogStore.Empty())
            .Resolve(CreateReviewRun(), Exited(0), ResultLine(DesignFindingCiting("design.1")));

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>();
    }

    [Fact]
    public void WhenAValidVerdictApproves_CompletesTheReviewWithTheDerivedOutcome()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(0), ResultLine(Verdict()));

        // Assert
        WorkerOutcome.ReviewCompleted completed = outcome.ShouldBeOfType<WorkerOutcome.ReviewCompleted>();
        completed.ShouldSatisfyAllConditions(
            () => completed.Verdict.Outcome.ShouldBe(ReviewVerdictOutcome.Approved),
            () => completed.Verdict.Facts.ShouldBe(Facts()));
    }
}
