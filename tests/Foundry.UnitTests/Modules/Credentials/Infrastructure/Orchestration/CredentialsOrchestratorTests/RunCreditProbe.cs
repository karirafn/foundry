using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.CreditProbe;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.Orchestration.CredentialsOrchestratorTests;

public sealed class RunCreditProbe
{
    private static CredentialsOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime);

    private static CreditProbeSpec OAuthSpec() =>
        new(
            AuthMode: new AuthMode.OAuth(null),
            Prompt: CreditProbeSpec.DefaultPrompt,
            TimeoutSeconds: CreditProbeSpec.DefaultTimeoutSeconds);

    private static CreditProbeSpec ApiKeySpec(string key = "sk-ant-test") =>
        new(
            AuthMode: new AuthMode.ApiKey(key),
            Prompt: CreditProbeSpec.DefaultPrompt,
            TimeoutSeconds: CreditProbeSpec.DefaultTimeoutSeconds);

    [Fact]
    public async Task WhenStarted_UsesLoginImageName()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Image.ShouldBe(WorkerImageNames.LoginImageName);
    }

    [Fact]
    public async Task WhenStarted_SetsTransientLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Labels.ShouldContainKey("foundry.transient");
        captured.Labels["foundry.transient"].ShouldBe("true");
    }

    [Fact]
    public async Task WhenStarted_SetsRoleCreditProbeLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Labels.ShouldContainKey("foundry.role");
        captured.Labels["foundry.role"].ShouldBe("credit-probe");
    }

    [Fact]
    public async Task WhenStarted_SetsAutoRemoveTrue()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.AutoRemove.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenStarted_CmdWrapsClaudeWithTimeout()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        string cmdStr = string.Join(" ", captured.Cmd);
        cmdStr.ShouldSatisfyAllConditions(
            () => cmdStr.ShouldContain($"timeout {CreditProbeSpec.DefaultTimeoutSeconds}"),
            () => cmdStr.ShouldContain("claude"),
            () => cmdStr.ShouldContain("-p"));
    }

    [Fact]
    public async Task WhenOAuthMode_MountsCredentialVolumeReadOnly()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

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
    public async Task WhenOAuthMode_SetsClaudeConfigDirEnv()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        string expectedEnv = $"{WorkerVolumeNames.ClaudeConfigDirEnvVar}={WorkerVolumeNames.ClaudeConfigContainerPath}";
        captured.Env.ShouldContain(expectedEnv);
    }

    [Fact]
    public async Task WhenApiKeyMode_InjectsAnthropicApiKeyEnv()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);
        CreditProbeSpec spec = ApiKeySpec("sk-ant-abc123");

        // Act
        await sut.RunCreditProbeAsync(spec, CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Env.ShouldContain("ANTHROPIC_API_KEY=sk-ant-abc123");
    }

    [Fact]
    public async Task WhenApiKeyMode_DoesNotMountCredentialVolume()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(ApiKeySpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<Mount>? mounts = captured.HostConfig.Mounts;
        bool hasCredVolume = mounts is not null
            && mounts.Any(m => m.Source == WorkerVolumeNames.CredentialVolumeName);
        hasCredVolume.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenContainerRunsSuccessfully_ReturnsOkWithLogs()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines("credit probe ran ok");
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<string> result = await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldContain("credit probe ran ok");
    }

    [Fact]
    public async Task WhenDockerThrows_ReturnsFailureWithRedactedMessage()
    {
        // Arrange
        DockerApiException exception = new(
            System.Net.HttpStatusCode.InternalServerError,
            "Docker daemon unavailable");
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithCreateAndStartThrowing(exception);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<string> result = await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Code.ShouldBe("Docker.CreditProbeStartFailed");
    }
}
