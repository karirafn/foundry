using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class StartLoginContainerAsync
{
    private static CredentialsOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime);

    [Fact]
    public async Task WhenStarted_UsesLoginImageName()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Image.ShouldBe(WorkerImageNames.LoginImageName);
    }

    [Fact]
    public async Task WhenStarted_SetsTtyFalse()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Tty.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenStarted_SetsWorkingDirToHomeNode()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.WorkingDir.ShouldBe(OnboardingSeed.DefaultWorkDir);
    }

    [Fact]
    public async Task WhenStarted_SetsClaudeConfigDirEnv()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        string expectedEnv = $"{WorkerVolumeNames.ClaudeConfigDirEnvVar}={WorkerVolumeNames.ClaudeConfigContainerPath}";
        captured.Env.ShouldContain(expectedEnv);
    }

    [Fact]
    public async Task WhenStarted_MountsCredentialVolume()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.ShouldContain(m =>
            m.Type == "volume"
            && m.Source == WorkerVolumeNames.CredentialVolumeName
            && m.Target == WorkerVolumeNames.ClaudeConfigContainerPath
            && !m.ReadOnly);
    }

    [Fact]
    public async Task WhenStarted_SetsLoginLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Labels.ShouldContainKey("foundry.login");
        captured.Labels["foundry.login"].ShouldBe("true");
    }

    [Fact]
    public async Task WhenStarted_SetsAttachStdoutAndStderr()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.ShouldSatisfyAllConditions(
            () => captured.AttachStdout.ShouldBeTrue(),
            () => captured.AttachStderr.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenStarted_CmdContainsFifoBootstrap()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.StartLoginContainerAsync(new LoginContainerSpec(TimeoutSeconds: 600), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        string cmdStr = string.Join(" ", captured.Cmd);
        cmdStr.ShouldSatisfyAllConditions(
            () => cmdStr.ShouldContain("mkfifo"),
            () => cmdStr.ShouldContain(LoginExecCommand.FifoPath),
            () => cmdStr.ShouldContain("sleep 600"),
            () => cmdStr.ShouldContain($"echo $! > {LoginExecCommand.SleepPidPath}"),
            () => cmdStr.ShouldContain("exec claude auth login --claudeai"),
            () => cmdStr.ShouldContain($"< {LoginExecCommand.FifoPath}"));
    }

    [Fact]
    public async Task WhenStarted_ReturnsContainerIdStringOnSuccess()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithContainerId("login-container-xyz");
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<string> result = await sut.StartLoginContainerAsync(
            new LoginContainerSpec(TimeoutSeconds: 600),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBe("login-container-xyz");
    }
}
