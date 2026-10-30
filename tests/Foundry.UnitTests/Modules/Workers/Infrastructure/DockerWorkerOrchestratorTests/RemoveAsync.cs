using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class RemoveAsync
{
    private static WorkerOptions DefaultOptions() => new()
    {
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime, Options.Create(DefaultOptions()));

    // ── StopAndRemoveAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task StopAndRemoveAsync_WhenContainerHasWorkerRunIdLabel_DeletesDataRootVolume()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithInspectWorkerRunId(runId.Value.ToString());
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StopAndRemoveAsync("container-abc", CancellationToken.None);

        // Assert
        runtime.LastRemovedVolumeName.ShouldBe(WorkerVolumeNames.DataRootVolumeName(runId));
    }

    [Fact]
    public async Task StopAndRemoveAsync_WhenContainerHasNoWorkerRunIdLabel_DoesNotDeleteVolume()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithInspectResponse(new ContainerInspectResponse
            {
                State = new ContainerState { Running = false },
                Config = new Config { Labels = new Dictionary<string, string>() },
            });
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StopAndRemoveAsync("container-abc", CancellationToken.None);

        // Assert
        runtime.LastRemovedVolumeName.ShouldBeNull();
    }

    [Fact]
    public async Task StopAndRemoveAsync_InspectsBeforeRemovingContainer()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithInspectWorkerRunId(runId.Value.ToString());
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StopAndRemoveAsync("container-abc", CancellationToken.None);

        // Assert
        runtime.LastInspectContainerId.ShouldBe("container-abc");
        runtime.LastRemoveContainerId.ShouldBe("container-abc");
    }

    // ── RemoveContainerAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RemoveContainerAsync_WhenContainerHasWorkerRunIdLabel_DeletesDataRootVolume()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithInspectWorkerRunId(runId.Value.ToString());
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RemoveContainerAsync("container-xyz", CancellationToken.None);

        // Assert
        runtime.LastRemovedVolumeName.ShouldBe(WorkerVolumeNames.DataRootVolumeName(runId));
    }

    [Fact]
    public async Task RemoveContainerAsync_WhenContainerHasNoWorkerRunIdLabel_DoesNotDeleteVolume()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithInspectResponse(new ContainerInspectResponse
            {
                State = new ContainerState { Running = false },
                Config = new Config { Labels = new Dictionary<string, string>() },
            });
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RemoveContainerAsync("container-xyz", CancellationToken.None);

        // Assert
        runtime.LastRemovedVolumeName.ShouldBeNull();
    }
}
