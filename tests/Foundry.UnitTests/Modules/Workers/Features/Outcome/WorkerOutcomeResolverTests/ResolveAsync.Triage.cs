using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;
using Foundry.Testing;

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

    private const string BlockerReason = "Needs it first.";

    private static ActiveRun CreateTriageRunSupplied(params int[] suppliedOpenIssueNumbers)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Triage);
        starting.RecordSuppliedOpenIssues(new TrackerContextBuilder().WithOpenIssueNumbers(suppliedOpenIssueNumbers).Build());
        return starting.Activate(ContainerId.From("container-triage"), branchName: null, MonitoredRepositoryId.New());
    }

    private static string BlockedOutput(IEnumerable<(int Issue, string Reason)> blockers)
    {
        string blockedBy = string.Join(",", blockers.Select(b => $$"""{"issue":{{b.Issue}},"reason":"{{b.Reason}}"}"""));
        return $$$"""{"type":"result","subtype":"success","is_error":false,"result":"Done","structured_output":{"outcome":"blocked","kind":"bug","blockedBy":[{{{blockedBy}}}]}}""";
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
    public async Task WhenABlockedVerdictNamesOnlySuppliedIssues_ReturnsTriageCompletedBlocked()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        string output = BlockedOutput([(15, BlockerReason), (16, BlockerReason)]);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRunSupplied(14, 15, 16),
            Exited(0),
            output,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.TriageCompleted>()
            .Verdict.ShouldBe(
                new TriageVerdict.Blocked(
                    TriageIssueKind.Bug,
                    [new TriageBlocker(15, BlockerReason), new TriageBlocker(16, BlockerReason)]));
    }

    [Fact]
    public async Task WhenABlockedVerdictNamesAnIssueOutsideTheSuppliedSet_FailsNamingTheNumber()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        string output = BlockedOutput([(15, BlockerReason), (99, BlockerReason)]);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRunSupplied(14, 15),
            Exited(0),
            output,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason
            .ShouldBe(FailureReason.VerdictInvalid.BlockerNotSupplied(99));
    }

    [Fact]
    public async Task WhenABlockedVerdictNamesAnIssueTwice_FailsNamingTheNumber()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        string output = BlockedOutput([(15, BlockerReason), (16, BlockerReason), (15, BlockerReason)]);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRunSupplied(15, 16),
            Exited(0),
            output,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason
            .ShouldBe(FailureReason.VerdictInvalid.BlockerDuplicated(15));
    }

    [Fact]
    public async Task WhenABlockedVerdictRunHasNoRecordedSuppliedSet_FailsWithoutBlamingTheModel()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        string output = BlockedOutput([(15, BlockerReason)]);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRun(),
            Exited(0),
            output,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason
            .ShouldBe(new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.NoSuppliedSetDetail));
    }

    [Fact]
    public async Task WhenABlockedVerdictIsRejected_TheFailureVoidsTheRecordedVerdict()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        string output = BlockedOutput([(99, BlockerReason)]);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRunSupplied(15),
            Exited(0),
            output,
            RunVerdict.Done(),
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().Verdict.ShouldBe(RunVerdict.Invalid());
    }

    [Fact]
    public async Task WhenABlockedVerdictNamesTheMostBlockersAnIssueRecords_ReturnsTriageCompleted()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        int[] numbers = [.. Enumerable.Range(1, TriageResult.Blocked.MaxBlockers)];
        string output = BlockedOutput(numbers.Select(number => (number, BlockerReason)));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRunSupplied(numbers),
            Exited(0),
            output,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.TriageCompleted>();
    }

    [Fact]
    public async Task WhenABlockedVerdictNamesMoreBlockersThanAnIssueRecords_Fails()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        int[] numbers = [.. Enumerable.Range(1, TriageResult.Blocked.MaxBlockers + 1)];
        string output = BlockedOutput(numbers.Select(number => (number, BlockerReason)));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRunSupplied(numbers),
            Exited(0),
            output,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason
            .ShouldBe(FailureReason.VerdictInvalid.BlockersExceedCap(TriageResult.Blocked.MaxBlockers));
    }

    [Fact]
    public async Task WhenABlockerReasonIsAtTheLengthCap_ReturnsTriageCompleted()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        string output = BlockedOutput([(15, new string('a', TriageBlocker.ReasonMaxLength))]);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRunSupplied(15),
            Exited(0),
            output,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.TriageCompleted>();
    }

    [Fact]
    public async Task WhenABlockerReasonExceedsTheLengthCap_FailsNamingTheNumber()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();
        string output = BlockedOutput([(15, new string('a', TriageBlocker.ReasonMaxLength + 1))]);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateTriageRunSupplied(15),
            Exited(0),
            output,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>().FailureReason
            .ShouldBe(FailureReason.VerdictInvalid.BlockerReasonTooLong(15, TriageBlocker.ReasonMaxLength));
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
