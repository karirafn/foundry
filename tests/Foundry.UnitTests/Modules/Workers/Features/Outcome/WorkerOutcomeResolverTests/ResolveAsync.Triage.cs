using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeResolverTests;

public sealed partial class ResolveAsync
{
    private const string ReadyOutput =
        """{"type":"result","subtype":"success","is_error":false,"result":"Done","structured_output":{"outcome":"ready","kind":"bug"}}""";

    private static ActiveRun CreateTriageRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Triage);
        return starting.Activate(ContainerId.From("container-triage"), branchName: null, MonitoredRepositoryId.New());
    }

    private static WorkerOutcomeResolver BuildTriageResolver() =>
        new(
            new NoProviderCallsQueries(),
            new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance),
            prRetryDelay: TimeSpan.Zero);

    [Fact]
    public async Task WhenATriageRunExitsZeroWithAValidVerdict_ReturnsTriageCompletedWithoutAMergeRequestLookup()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRun(),
            Exited(0),
            ReadyOutput,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.TriageCompleted>()
            .Verdict.ShouldBe(new TriageVerdict.Ready(TriageIssueKind.Bug));
    }

    [Fact]
    public async Task WhenATriageRunExitsZeroWithoutAVerdict_FailsAsAMissingVerdict()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        const string output = """{"type":"result","subtype":"success","is_error":false,"result":"Done"}""";

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(CreateTriageRun(), Exited(0), output, verdict: null, TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason
            .ShouldBe(new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.MissingDetail));
    }

    [Fact]
    public async Task WhenATriageRunExitsZeroWithAnInvalidVerdict_FailsAsAnInvalidVerdict()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        const string output =
            """{"type":"result","subtype":"success","is_error":false,"result":"Done","structured_output":{"outcome":"ready"}}""";

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(CreateTriageRun(), Exited(0), output, verdict: null, TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.ShouldSatisfyAllConditions(
            () => failure.FailureReason.ShouldBe(new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.InvalidDetail)),
            () => failure.FailureReason.CategoryToken.ShouldBe(FailureCategory.VerdictInvalidToken));
    }

    [Fact]
    public async Task WhenATriageRunExitsNonZero_FailsWithoutReadingItsVerdict()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(CreateTriageRun(), Exited(1), ReadyOutput, verdict: null, TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason.ShouldBeOfType<FailureReason.NonZeroExit>();
    }

    [Fact]
    public async Task WhenATriageRunContainerIsGone_Fails()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(CreateTriageRun(), status: null, containerOutput: null, verdict: null, TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }

    [Fact]
    public async Task WhenATriageRunIsCancelled_ThereIsNoMergeRequestToFind()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();

        // Act
        WorkerOutcome? outcome = await sut.TryResolveExistingPullRequestAsync(
            CreateTriageRun(),
            containerOutput: null,
            summary: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeNull();
    }

    [Fact]
    public async Task WhenATriageRunIsStopped_ItFailsWithTheStopReason()
    {
        // Arrange — no branch, so no commit lookup either.

        // Act
        WorkerOutcome outcome = await StoppedRunOutcomeBuilder.BuildAsync(
            CreateTriageRun(),
            new FailureReason.TimedOut(),
            containerOutput: null,
            summary: null,
            new NoProviderCallsQueries(),
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason.ShouldBeOfType<FailureReason.TimedOut>();
    }

    /// <summary>Fails the test on any provider call: a triage run has no branch to look up.</summary>
    private sealed class NoProviderCallsQueries : IPostExitProviderQueries
    {
        public Task<Result<bool>> CreateBranchAsync(MonitoredRepositoryId repositoryId, string branchName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No provider call is expected.");

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(MonitoredRepositoryId repositoryId, string branchName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No merge-request lookup is expected for a triage run.");

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(MonitoredRepositoryId repositoryId, string branchName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No commit lookup is expected for a triage run.");
    }
}
