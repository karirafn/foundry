using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

using IntegrationWorkerRunFailed = Foundry.Modules.Workers.Contracts.WorkerRunFailed;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class CancelDetection : WorkerDispatchServiceTestBase
{
    private const string PrUrl = "https://github.com/owner/repo/pull/7";

    private static readonly DateTimeOffset CancelRequestedAt = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private ActiveRun SeedCancelRequestedRun(string containerId = "container-cancel")
    {
        ActiveRun seeded = SeedActiveRun(containerId);

        using FoundryDbContext db = CreateDbContext();
        ActiveRun run = db.Set<ActiveRun>().Single(r => r.Id == seeded.Id);
        run.RequestCancel(CancelRequestedAt);
        db.SaveChanges();
        return run;
    }

    private async Task<WorkerRun> LoadSingleRunAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun? run = await db.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        return run.ShouldNotBeNull();
    }

    private WorkerDispatchService BuildService(
        CancelStubOrchestrator orchestrator,
        StubPostExitProviderQueries? providerQueries = null,
        int timeoutMinutes = 120,
        CapturingIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        return base.BuildService(
            orchestrator,
            integrationEventDispatcher: integrationEventDispatcher,
            settingsQueries: new StubGlobalSettingsQueries(maxConcurrent: 3, timeoutMinutes: timeoutMinutes),
            postExitProviderQueries: providerQueries);
    }

    [Fact]
    public async Task WhenRunningWithoutCancelRequest_LeavesRunActiveAndContainerRunning()
    {
        // Arrange
        SeedActiveRun("container-no-cancel");
        CancelStubOrchestrator orchestrator = CancelStubOrchestrator.Running();
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadSingleRunAsync()).ShouldBeOfType<ActiveRun>();
        orchestrator.CallOrder.ShouldNotContain("stop_container");
    }

    [Fact]
    public async Task WhenCancelRequestedAndNoPrAndNoCommits_FailsRunAsCancelled()
    {
        // Arrange
        SeedCancelRequestedRun();
        CancelStubOrchestrator orchestrator = CancelStubOrchestrator.Running();
        WorkerDispatchService sut = BuildService(
            orchestrator,
            new StubPostExitProviderQueries(hasCommits: false));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        FailedRun failed = (await LoadSingleRunAsync()).ShouldBeOfType<FailedRun>();
        failed.ShouldSatisfyAllConditions(
            () => failed.Reason.ShouldBeOfType<FailureReason.Cancelled>(),
            () => failed.BranchName.ShouldNotBeNull(),
            () => failed.CancelRequestedAt.ShouldBe(CancelRequestedAt));
    }

    [Fact]
    public async Task WhenCancelRequestedAndNoPrAndNoCommits_PublishesFailureWithoutBranch()
    {
        // Arrange
        SeedCancelRequestedRun();
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            CancelStubOrchestrator.Running(),
            new StubPostExitProviderQueries(hasCommits: false),
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — no branch: the issue lands in FailedIssue (fresh retry)
        IntegrationWorkerRunFailed failed = dispatcher.Captured.OfType<IntegrationWorkerRunFailed>().ShouldHaveSingleItem();
        failed.ShouldSatisfyAllConditions(
            () => failed.Category.ShouldBe(FailureCategory.CancelledToken),
            () => failed.BranchName.ShouldBeNull());
    }

    [Fact]
    public async Task WhenCancelRequestedAndBranchHasCommits_PublishesContinuableFailureWithBranch()
    {
        // Arrange
        SeedCancelRequestedRun();
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            CancelStubOrchestrator.Running(),
            new StubPostExitProviderQueries(hasCommits: true),
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — a branch on the event routes the issue to ContinuableFailedIssue
        IntegrationWorkerRunFailed failed = dispatcher.Captured.OfType<IntegrationWorkerRunFailed>().ShouldHaveSingleItem();
        failed.ShouldSatisfyAllConditions(
            () => failed.Category.ShouldBe(FailureCategory.CancelledToken),
            () => failed.BranchName.ShouldBe("feat/1-default"));
    }

    [Fact]
    public async Task WhenCancelRequestedAndCommitLookupFailsTransiently_PublishesContinuableFailure()
    {
        // Arrange
        SeedCancelRequestedRun();
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            CancelStubOrchestrator.Running(),
            new StubPostExitProviderQueries(hasCommitsError: new Error("Provider.Unavailable", "boom")),
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        IntegrationWorkerRunFailed failed = dispatcher.Captured.OfType<IntegrationWorkerRunFailed>().ShouldHaveSingleItem();
        failed.BranchName.ShouldBe("feat/1-default");
    }

    [Fact]
    public async Task WhenCancelRequestedAndBranchNotFound_PublishesFailureWithoutBranch()
    {
        // Arrange
        SeedCancelRequestedRun();
        CapturingIntegrationEventDispatcher dispatcher = new();
        Error notFound = new("Provider.BranchNotFound", "Branch not found") { Kind = ErrorKind.NotFound };
        WorkerDispatchService sut = BuildService(
            CancelStubOrchestrator.Running(),
            new StubPostExitProviderQueries(hasCommitsError: notFound),
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        IntegrationWorkerRunFailed failed = dispatcher.Captured.OfType<IntegrationWorkerRunFailed>().ShouldHaveSingleItem();
        failed.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenCancelRequested_StopsCapturesLogsThenRemovesInOneTick()
    {
        // Arrange
        SeedCancelRequestedRun("container-cancel-order");
        CancelStubOrchestrator orchestrator = CancelStubOrchestrator.Running(logs: "partial output");
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.CallOrder.ShouldBe(["stop_container", "get_logs", "stop_and_remove"]);
        FailedRun failed = (await LoadSingleRunAsync()).ShouldBeOfType<FailedRun>();
        failed.ContainerOutput.ShouldBe("partial output");
    }

    [Fact]
    public async Task WhenCancelRequestedAndPrIsOpen_CompletesRunAsReview()
    {
        // Arrange
        SeedCancelRequestedRun();
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            CancelStubOrchestrator.Running(),
            new StubPostExitProviderQueries(
                hasCommits: true,
                mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)),
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — the worker finished before the cancel took effect
        CompletedRun completed = (await LoadSingleRunAsync()).ShouldBeOfType<CompletedRun>();
        completed.ShouldSatisfyAllConditions(
            () => completed.PullRequestUrl.ShouldBe(PullRequestUrl.From(PrUrl)),
            () => completed.CancelRequestedAt.ShouldBe(CancelRequestedAt));
        dispatcher.Captured.OfType<WorkerRunCompleted>().ShouldHaveSingleItem()
            .MergeState.ShouldBe(WorkerRunMergeState.Open);
    }

    [Fact]
    public async Task WhenCancelRequestedAndTimeoutElapsed_CancelWins()
    {
        // Arrange — timeoutMinutes: 0 means the run is also past its timeout
        SeedCancelRequestedRun();
        WorkerDispatchService sut = BuildService(CancelStubOrchestrator.Running(), timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        FailedRun failed = (await LoadSingleRunAsync()).ShouldBeOfType<FailedRun>();
        failed.Reason.ShouldBeOfType<FailureReason.Cancelled>();
    }

    [Fact]
    public async Task WhenCancelRequestedButContainerAlreadyExited_ResolvesRealOutcome()
    {
        // Arrange — container exited 0 with an open PR before the tick
        SeedCancelRequestedRun();
        CancelStubOrchestrator orchestrator = CancelStubOrchestrator.Exited(exitCode: 0);
        WorkerDispatchService sut = BuildService(
            orchestrator,
            new StubPostExitProviderQueries(
                hasCommits: true,
                mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadSingleRunAsync()).ShouldBeOfType<CompletedRun>();
        orchestrator.CallOrder.ShouldNotContain("stop_container");
    }

    [Fact]
    public async Task WhenCancelRequestedButContainerExitedNonZero_KeepsRealFailureReason()
    {
        // Arrange
        SeedCancelRequestedRun();
        WorkerDispatchService sut = BuildService(
            CancelStubOrchestrator.Exited(exitCode: 1),
            new StubPostExitProviderQueries(hasCommits: false));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        FailedRun failed = (await LoadSingleRunAsync()).ShouldBeOfType<FailedRun>();
        failed.Reason.ShouldNotBeOfType<FailureReason.Cancelled>();
    }

    [Fact]
    public async Task WhenCancelRequestedAndContainerNotFound_ResolvesThroughNotFoundPath()
    {
        // Arrange
        SeedCancelRequestedRun();
        WorkerDispatchService sut = BuildService(
            CancelStubOrchestrator.Missing(),
            new StubPostExitProviderQueries(hasCommits: false));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        FailedRun failed = (await LoadSingleRunAsync()).ShouldBeOfType<FailedRun>();
        failed.Reason.ShouldNotBeOfType<FailureReason.Cancelled>();
    }

    [Fact]
    public async Task WhenCancelRequestedAndDaemonUnreachable_LeavesRequestPendingUntilReachable()
    {
        // Arrange
        SeedCancelRequestedRun();
        CancelStubOrchestrator orchestrator = CancelStubOrchestrator.Unreachable();
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act — first tick: daemon unreachable
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — nothing happened, request still persisted
        ActiveRun stillActive = (await LoadSingleRunAsync()).ShouldBeOfType<ActiveRun>();
        stillActive.CancelRequestedAt.ShouldBe(CancelRequestedAt);
        orchestrator.CallOrder.ShouldBeEmpty();

        // Act — daemon comes back
        orchestrator.Probe = new WorkerStatusProbe.Available(
            new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null));
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        FailedRun failed = (await LoadSingleRunAsync()).ShouldBeOfType<FailedRun>();
        failed.Reason.ShouldBeOfType<FailureReason.Cancelled>();
    }

    [Fact]
    public async Task WhenStopContainerThrows_StillAppliesCancelledOutcome()
    {
        // Arrange — stop failure proceeds like timeout; stop-and-remove and the orphan sweep follow up
        SeedCancelRequestedRun();
        CancelStubOrchestrator orchestrator = CancelStubOrchestrator.Running();
        orchestrator.StopContainerThrows = true;
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        FailedRun failed = (await LoadSingleRunAsync()).ShouldBeOfType<FailedRun>();
        failed.Reason.ShouldBeOfType<FailureReason.Cancelled>();
        orchestrator.CallOrder.ShouldContain("stop_and_remove");
    }

    internal sealed class CancelStubOrchestrator(WorkerStatusProbe probe, string? logs = null) : IWorkerOrchestrator
    {
        private readonly List<string> _callOrder = [];

        public WorkerStatusProbe Probe { get; set; } = probe;

        public bool StopContainerThrows { get; set; }

        public IReadOnlyList<string> CallOrder => _callOrder;

        public static CancelStubOrchestrator Running(string? logs = null)
            => new(new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null)), logs);

        public static CancelStubOrchestrator Exited(int exitCode)
            => new(new WorkerStatusProbe.Available(
                new WorkerStatus(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow)));

        public static CancelStubOrchestrator Missing() => new(new WorkerStatusProbe.NotFound());

        public static CancelStubOrchestrator Unreachable() => new(new WorkerStatusProbe.Unreachable());

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in cancel tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        {
            _callOrder.Add("stop_and_remove");
            return Task.CompletedTask;
        }

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult(Probe);

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
        {
            _callOrder.Add("get_logs");
            return Task.FromResult(logs);
        }

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
        {
            _callOrder.Add("stop_container");

            if (StopContainerThrows)
            {
                throw new InvalidOperationException("StopContainer failed");
            }

            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
        {
            _callOrder.Add("remove_container");
            return Task.CompletedTask;
        }

        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
