using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Modules.Workers.Contracts;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class GetCredentialVolumeAuthStatusAsync
{
    private static CredentialsOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime);

    [Fact]
    public async Task WhenStarted_MountsCredentialVolumeReadOnly()
    {
        // Arrange
        string validJson =
            """{"loggedIn":true,"email":"user@example.com","orgName":"Org","subscriptionType":"pro"}""";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithContainerId("helper-container-id")
            .WithExecCaptureStdout(validJson);
        CredentialsOrchestrator sut = BuildSut(runtime);

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

    [Fact]
    public async Task WhenStarted_HelperContainerSetsTransientLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Labels.ShouldNotBeNull();
        captured.Labels.ShouldContainKey("foundry.transient");
        captured.Labels["foundry.transient"].ShouldBe("true");
    }

    [Fact]
    public async Task WhenStarted_HelperContainerSetsRoleCredentialHelperLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Labels.ShouldNotBeNull();
        captured.Labels.ShouldContainKey("foundry.role");
        captured.Labels["foundry.role"].ShouldBe("credential-helper");
    }

    [Fact]
    public async Task WhenStarted_HelperContainerSetsAutoRemoveTrue()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.AutoRemove.ShouldBeTrue();
    }
}
