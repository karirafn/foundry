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
}
