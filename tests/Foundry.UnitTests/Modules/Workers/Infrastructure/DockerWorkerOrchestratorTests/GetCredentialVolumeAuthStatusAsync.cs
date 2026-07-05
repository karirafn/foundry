using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class GetCredentialVolumeAuthStatusAsync
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
    public async Task WhenStarted_MountsCredentialVolumeReadOnly()
    {
        // Arrange
        string validJson =
            """{"loggedIn":true,"email":"user@example.com","orgName":"Org","subscriptionType":"pro"}""";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithContainerId("helper-container-id")
            .WithExecCaptureStdout(validJson);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.ShouldContain(m =>
            m.Type == "volume"
            && m.Source == WorkerVolumeNames.CredentialVolumeName
            && m.Target == WorkerVolumeNames.ClaudeConfigContainerPath
            && m.ReadOnly);
    }
}
