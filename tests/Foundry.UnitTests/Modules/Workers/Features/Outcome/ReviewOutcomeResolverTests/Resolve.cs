using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ReviewOutcomeResolverTests;

public sealed class Resolve
{
    private const string VerdictOutput =
        """{"type":"result","subtype":"success","is_error":false,"result":"Done","structured_output":{"summary":"Looks right."}}""";

    private static ReviewOutcomeResolver CreateSut() =>
        new(new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance));

    private static ActiveRun CreateReviewRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        return starting.Activate(ContainerId.From("container-review"), branchName: null, MonitoredRepositoryId.New());
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
    public void WhenAReviewRunExitsZeroWithAVerdict_FailsAsVerdictInvalidBecauseNoReviewVerdictIsReadYet()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(0), VerdictOutput);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.VerdictInvalid>()
            .Detail.ShouldBe(ReviewOutcomeResolver.NotReadDetail);
    }

    [Fact]
    public void WhenAReviewRunExitsNonZero_ClassifiesTheExitAsForEveryRun()
    {
        // Act
        WorkerOutcome outcome = CreateSut().Resolve(CreateReviewRun(), Exited(3), VerdictOutput);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.NonZeroExit>()
            .ExitCode.ShouldBe(3);
    }
}
