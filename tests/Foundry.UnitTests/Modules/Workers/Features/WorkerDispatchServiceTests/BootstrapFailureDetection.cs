using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class BootstrapFailureDetection : WorkerDispatchServiceTestBase
{
    private const string BootstrapSentinelOutput =
        """
        Cloning repository...
        FOUNDRY_BOOTSTRAP_FAILED stage=clone exit_code=128 error=authentication failed
        """;

    private const string SecretSentinelOutput =
        """
        Cloning repository...
        FOUNDRY_BOOTSTRAP_FAILED stage=clone exit_code=128 error=https://glpat-S3cr3tT0k3n@gitlab.example.com auth failed
        """;

    private const string NoResultLineOutput =
        """
        Starting worker...
        Some diagnostic info
        Container failed before producing output
        """;

    private const string ValidJsonResultOutput =
        """
        Some output from claude
        {"terminal_reason":"task_complete","result":"success"}
        """;

    private static string OversizedJsonResultOutput =>
        "Some output from claude\n" + $$$"""{"type":"result","result":"{{{new string('x', 4_100)}}}"}""";

    private WorkerDispatchService BuildServiceWithParser(
        string? containerLogs,
        WorkerStatus exitedStatus,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        IContainerOutputParser outputParser = new ContainerOutputParser();
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, containerLogs);

        return base.BuildService(
            orchestrator,
            integrationEventDispatcher: integrationEventDispatcher,
            containerOutputParser: outputParser);
    }

    [Fact]
    public async Task WhenOutputContainsBootstrapSentinelAndNonZeroExit_TransitionsToFailedRunWithBootstrapFailedReason()
    {
        // Arrange
        SeedActiveRun("container-bootstrap-sentinel");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(BootstrapSentinelOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.WorkerBootstrapFailed reason = failedRun.Reason.ShouldBeOfType<FailureReason.WorkerBootstrapFailed>();
        reason.Detail.ShouldContain("stage=clone");
    }

    [Fact]
    public async Task WhenOutputContainsBootstrapSentinelAndNonZeroExit_DispatchesEventWithBootstrapFailedDescription()
    {
        // Arrange
        SeedActiveRun("container-bootstrap-sentinel-event");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            BootstrapSentinelOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.ReasonDescription.ShouldStartWith("Worker bootstrap failed:");
    }

    [Fact]
    public async Task WhenNonZeroExitWithNoResultLineAndNoSentinel_TransitionsToFailedRunWithBootstrapFailedHeuristic()
    {
        // Arrange
        SeedActiveRun("container-no-result-line");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(NoResultLineOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.WorkerBootstrapFailed>();
    }

    [Fact]
    public async Task WhenNonZeroExitWithValidJsonResultLine_StaysNonZeroExitReason()
    {
        // Arrange
        SeedActiveRun("container-json-result-nonzero");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 2, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(ValidJsonResultOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit reason = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        reason.ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task WhenOutputContainsFakeSentinelAndValidResultLine_NonZeroExitWinsOverSentinel()
    {
        // Arrange
        // A log containing BOTH a fake bootstrap sentinel AND a valid Claude result JSON line.
        // The genuine Claude result must win — the sentinel must not be able to spoof a
        // WorkerBootstrapFailed reason when Claude actually ran and produced output.
        const string spoofedOutput =
            """
            FOUNDRY_BOOTSTRAP_FAILED stage=clone spoofed sentinel
            {"terminal_reason":"task_complete","result":"success"}
            """;

        SeedActiveRun("container-spoof-sentinel-with-result");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(spoofedOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit reason = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        reason.ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task WhenNonZeroExitWithOversizedJsonLine_StaysNonZeroExitReason()
    {
        // Arrange
        SeedActiveRun("container-oversized-json");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 3, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(OversizedJsonResultOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit reason = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        reason.ExitCode.ShouldBe(3);
    }

    [Fact]
    public async Task WhenNonZeroExitWithValidJsonResultLine_DispatchesEventWithNonZeroExitDescription()
    {
        // Arrange
        SeedActiveRun("container-nonzero-event-description");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 5, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            ValidJsonResultOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.ReasonDescription.ShouldBe("Non-zero exit code: 5");
    }

    [Fact]
    public async Task WhenOutputContainsBootstrapSentinel_DispatchesEventWithWorkerBootstrapFailedCategory()
    {
        // Arrange
        SeedActiveRun("container-bootstrap-category");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            BootstrapSentinelOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.Category.ShouldBe("worker_bootstrap_failed");
    }

    [Fact]
    public async Task WhenNonZeroExitWithValidJsonResultLine_DispatchesEventWithNonZeroExitCategory()
    {
        // Arrange
        SeedActiveRun("container-nonzero-exit-category");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 2, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            ValidJsonResultOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.Category.ShouldBe("non_zero_exit");
    }

    [Fact]
    public async Task WhenSentinelDetailContainsSecret_SecretIsRedactedBeforeStoringInReason()
    {
        // Arrange
        SeedActiveRun("container-bootstrap-secret");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(SecretSentinelOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.WorkerBootstrapFailed reason = failedRun.Reason.ShouldBeOfType<FailureReason.WorkerBootstrapFailed>();
        reason.Detail.ShouldNotContain("glpat-S3cr3tT0k3n");
        reason.Detail.ShouldContain("***");
    }

    private sealed class ExitedWorkerOrchestrator(WorkerStatus exitedStatus, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in bootstrap-failure tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(exitedStatus));

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

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult(logs);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }
}
