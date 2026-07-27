using System.Runtime.CompilerServices;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class ImageBuildGate : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenImageBuildStatusIsBuilding_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurableImageBuildStatusQueries(ImageBuildStatus.Building));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenImageBuildStatusIsFailed_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurableImageBuildStatusQueries(ImageBuildStatus.Failed));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenImageBuildStatusIsIdle_DispatchesWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurableImageBuildStatusQueries(ImageBuildStatus.Idle));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is WorkerCapacityAvailable);
    }

    private sealed class ConfigurableImageBuildStatusQueries(ImageBuildStatus imageBuildStatus) : IGlobalSettingsQueries
    {
        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult<GlobalSettingsSummary?>(null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(3);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(120);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DispatchPauseState(null, false, true));

        public Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(imageBuildStatus);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class NullWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("default-container")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null)));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
