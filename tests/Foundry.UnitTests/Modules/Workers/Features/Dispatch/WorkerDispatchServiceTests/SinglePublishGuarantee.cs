using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

/// <summary>
/// Tests that a single failure publishes the WorkerRunFailed integration event exactly once,
/// guarding against double-publish from simultaneous bridge handler and manual dispatch paths.
/// </summary>
public sealed class SinglePublishGuarantee : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenActiveRunFailsWithNoCommits_PublishesIntegrationEventExactlyOnce()
    {
        // Arrange — exited container with no commits → Failure outcome → null branch
        SeedActiveRun("container-terminal-once");
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new ExitedContainerOrchestrator(exitCode: 1),
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — exactly one WorkerRunFailed integration event, not two
        capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenActiveRunFailsWithCommits_PublishesIntegrationEventExactlyOnce()
    {
        // Arrange — exited container with commits → ContinuableFailure → non-null branch
        SeedActiveRun("container-continuable-once", branchName: "feat/44-partial");
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new ExitedContainerOrchestrator(exitCode: 1),
            postExitProviderQueries: new StubPostExitProviderQueries(hasCommits: true),
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — exactly one WorkerRunFailed integration event with non-null branch
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/44-partial");
    }

    [Fact]
    public async Task WhenTimeoutFailure_PublishesIntegrationEventExactlyOnce()
    {
        // Arrange — running container that times out → single event regardless of commit state
        SeedActiveRun("container-timeout-once-only");
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new RunningContainerOrchestrator(),
            settingsQueries: new StubGlobalSettingsQueries(timeoutMinutes: 0),
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — exactly one WorkerRunFailed integration event
        capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
    }

    // ─── Stubs ───────────────────────────────────────────────────────────────────

    private sealed class ExitedContainerOrchestrator(int exitCode) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(
                new WorkerStatusProbe.Available(
                    new WorkerStatus(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow)));

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

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RunningContainerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(
                new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null)));

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

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
