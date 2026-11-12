using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeApplierTests;

public sealed class ApplyAsync : WorkerDispatchServiceTestBase
{
    private const string ContainerId = "container-apply";
    private const string Branch = "feat/1-apply";

    public static TheoryData<string> TerminalOutcomes => ["Completed", "Review", "NeedsInput", "Unchanged", "ContinuableFailure", "Failure"];

    [Theory]
    [MemberData(nameof(TerminalOutcomes))]
    public async Task WhenOutcomeIsTerminal_TransitionsRunAndStopsAndRemovesContainer(string outcomeName)
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        RecordingOrchestrator orchestrator = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, orchestrator);

        // Act
        await sut.ApplyAsync(BuildOutcome(outcomeName), activeRun, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldNotBeOfType<ActiveRun>();
        orchestrator.StopAndRemoveCalls.ShouldBe([ContainerId]);
    }

    [Theory]
    [MemberData(nameof(TerminalOutcomes))]
    public async Task WhenAVerdictIsGiven_RecordsItOnTheTerminalRunWithItsQuestions(string outcomeName)
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator());
        RunVerdict verdict = RunVerdict.NeedsInput(
        [
            new RunVerdictQuestion("Which database?", "The issue names both SQLite and Postgres.", "Postgres."),
            new RunVerdictQuestion("Keep the old endpoint?", string.Empty, "Yes."),
        ]);

        // Act
        await sut.ApplyAsync(BuildOutcome(outcomeName), activeRun, verdict, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldSatisfyAllConditions(
            () => run.VerdictStatus.ShouldBe(RunVerdictStatus.NeedsInput),
            () => run.Verdict.ShouldBe(verdict),
            () => run.VerdictQuestions.ShouldNotBeNull().Count.ShouldBe(2));
    }

    [Theory]
    [InlineData(RunVerdictStatus.Done)]
    [InlineData(RunVerdictStatus.Missing)]
    [InlineData(RunVerdictStatus.Invalid)]
    public async Task WhenAVerdictWithoutQuestionsIsGiven_RecordsItsStatusAndNoQuestions(RunVerdictStatus status)
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator());
        RunVerdict verdict = status switch
        {
            RunVerdictStatus.Done => RunVerdict.Done(),
            RunVerdictStatus.Missing => RunVerdict.Missing(),
            _ => RunVerdict.Invalid(),
        };

        // Act
        await sut.ApplyAsync(BuildOutcome("Review"), activeRun, verdict, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldSatisfyAllConditions(
            () => run.ShouldBeOfType<CompletedRun>().PullRequestUrl.ShouldNotBeNull(),
            () => run.VerdictStatus.ShouldBe(status),
            () => run.VerdictQuestions.ShouldBeNull());
    }

    [Fact]
    public async Task WhenNoVerdictIsGiven_RecordsNone()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator());

        // Act
        await sut.ApplyAsync(BuildOutcome("Completed"), activeRun, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldSatisfyAllConditions(
            () => run.VerdictStatus.ShouldBeNull(),
            () => run.Verdict.ShouldBeNull());
    }

    public static TheoryData<string> CompletingOutcomes => ["Completed", "Review", "Unchanged"];

    [Theory]
    [MemberData(nameof(CompletingOutcomes))]
    public async Task WhenOutcomeCompletesTheRun_PersistsTheContainerOutput(string outcomeName)
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator());
        const string containerOutput = "2026-11-09T10:00:00.000000000Z line one\n2026-11-09T10:20:00.000000000Z line two\n";

        // Act
        await sut.ApplyAsync(BuildOutcome(outcomeName, containerOutput), activeRun, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>().ContainerOutput.ShouldBe(containerOutput);
    }

    [Theory]
    [MemberData(nameof(CompletingOutcomes))]
    public async Task WhenOutcomeCompletesTheRunWithoutOutput_CompletesWithNullContainerOutput(string outcomeName)
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator());

        // Act
        await sut.ApplyAsync(BuildOutcome(outcomeName), activeRun, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>().ContainerOutput.ShouldBeNull();
    }

    [Fact]
    public async Task WhenOutcomeNeedsInput_PublishesTheQuestionsAndWhatTheRunLeftOnWorkerRunCompleted()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        CapturingIntegrationEventDispatcher dispatcher = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator(), dispatcher);

        // Act
        await sut.ApplyAsync(BuildOutcome("NeedsInput"), activeRun, TestContext.Current.CancellationToken);

        // Assert
        WorkerRunCompleted completed = dispatcher.Captured.OfType<WorkerRunCompleted>().ShouldHaveSingleItem();
        completed.ShouldSatisfyAllConditions(
            () => completed.MergeState.ShouldBe(WorkerRunMergeState.Open),
            () => completed.PullRequestUrl.ShouldBe("https://github.com/acme/repo/pull/1"),
            () => completed.InputRequest.ShouldNotBeNull().BranchHasCommits.ShouldBeTrue(),
            () => completed.InputRequest!.Questions.ShouldBe([new InputQuestion("Which database?", "Two are configured.", "Postgres.")]));
    }

    [Fact]
    public async Task WhenATriageRunCompletes_PublishesTriageRunCompletedInsteadOfWorkerRunCompleted()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        CapturingIntegrationEventDispatcher dispatcher = new();
        RecordingOrchestrator orchestrator = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, orchestrator, dispatcher);
        WorkerOutcome outcome = new WorkerOutcome.TriageCompleted(
            new TriageVerdict.NeedsInput(
                TriageIssueKind.Refactor,
                [new RunVerdictQuestion("Which module?", "Two are named.", "Issues.")]),
            ContainerOutput: null,
            Summary: null);

        // Act
        await sut.ApplyAsync(outcome, activeRun, TestContext.Current.CancellationToken);

        // Assert
        TriageRunCompleted completed = dispatcher.Captured.OfType<TriageRunCompleted>().ShouldHaveSingleItem();
        completed.ShouldSatisfyAllConditions(
            () => completed.IssueId.ShouldBe(activeRun.IssueId.Value),
            () => completed.WorkerRunId.ShouldBe(activeRun.Id),
            () => completed.Result.ShouldBeOfType<TriageResult.NeedsInput>().Kind.ShouldBe(TriageKind.Refactor),
            () => ((TriageResult.NeedsInput)completed.Result).Questions.ShouldBe([new InputQuestion("Which module?", "Two are named.", "Issues.")]),
            () => dispatcher.Captured.OfType<WorkerRunCompleted>().ShouldBeEmpty(),
            () => orchestrator.StopAndRemoveCalls.ShouldBe([ContainerId]));
    }

    [Fact]
    public async Task WhenOutcomeIsIndeterminate_ThrowsWithoutTouchingContainer()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        RecordingOrchestrator orchestrator = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, orchestrator);

        // Act
        Func<Task> act = () => sut.ApplyAsync(
            new WorkerOutcome.Indeterminate(new Error("Test", "transient")),
            activeRun,
            TestContext.Current.CancellationToken);

        // Assert
        await act.ShouldThrowAsync<System.Diagnostics.UnreachableException>();
        orchestrator.StopAndRemoveCalls.ShouldBeEmpty();
    }

    private static WorkerOutcomeApplier BuildApplier(
        FoundryDbContext db,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
        => new(
            db,
            orchestrator,
            integrationEventDispatcher ?? new NullIntegrationEventDispatcher(),
            new NullIntegrationEventProcessor(),
            new NullDomainEventDispatcher(),
            TimeProvider.System,
            NullLogger<WorkerOutcomeApplier>.Instance);

    private static WorkerOutcome BuildOutcome(string name, string? containerOutput = null)
    {
        BranchName branch = BranchName.From(Branch);
        PullRequestUrl pullRequestUrl = PullRequestUrl.From("https://github.com/acme/repo/pull/1");

        return name switch
        {
            "Completed" => new WorkerOutcome.Completed(branch, pullRequestUrl, containerOutput, null),
            "Review" => new WorkerOutcome.Review(branch, pullRequestUrl, containerOutput, null),
            "NeedsInput" => new WorkerOutcome.NeedsInput(
                branch,
                pullRequestUrl,
                BranchHasCommits: true,
                [new RunVerdictQuestion("Which database?", "Two are configured.", "Postgres.")],
                containerOutput,
                null),
            "Unchanged" => new WorkerOutcome.Unchanged(branch, containerOutput, null),
            "ContinuableFailure" => new WorkerOutcome.ContinuableFailure(branch, new FailureReason.TimedOut(), null, null),
            "Failure" => new WorkerOutcome.Failure(new FailureReason.TimedOut(), null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingOrchestrator : IWorkerOrchestrator
    {
        private readonly List<string> _stopAndRemoveCalls = [];

        public IReadOnlyList<string> StopAndRemoveCalls => _stopAndRemoveCalls;

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        {
            _stopAndRemoveCalls.Add(containerId);
            return Task.CompletedTask;
        }

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.NotFound());

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
