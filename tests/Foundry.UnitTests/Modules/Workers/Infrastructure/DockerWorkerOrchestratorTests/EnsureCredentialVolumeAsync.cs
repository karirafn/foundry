using Docker.DotNet.Models;

using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class EnsureCredentialVolumeAsync
{
    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime, Options.Create(DefaultOptions()));

    [Fact]
    public async Task WhenVolumeDoesNotExist_CreatesVolumeWithCorrectName()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.EnsureCredentialVolumeAsync(CancellationToken.None);

        // Assert
        VolumesCreateParameters captured = runtime.LastCreateVolumeParameters.ShouldNotBeNull();
        captured.Name.ShouldBe("foundry-claude-credentials");
    }

    [Fact]
    public async Task WhenVolumeDoesNotExist_CreatesVolumeWithManagedLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.EnsureCredentialVolumeAsync(CancellationToken.None);

        // Assert
        VolumesCreateParameters captured = runtime.LastCreateVolumeParameters.ShouldNotBeNull();
        captured.Labels.ShouldNotBeNull();
        captured.Labels["foundry.managed"].ShouldBe("true");
    }
}
