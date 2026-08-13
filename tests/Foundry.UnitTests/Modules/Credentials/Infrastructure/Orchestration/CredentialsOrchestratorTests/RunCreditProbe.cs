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
    public async Task WhenStarted_CmdIsDirectArgvWithTimeout()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert — argv form: ["timeout", "<seconds>", "claude", "-p", "<prompt>"]
        // No shell interpretation of prompt or timeout value.
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<string> cmd = captured.Cmd.ShouldNotBeNull();
        cmd.ShouldSatisfyAllConditions(
            () => cmd[0].ShouldBe("timeout"),
            () => cmd[1].ShouldBe(CreditProbeSpec.DefaultTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            () => cmd[2].ShouldBe("claude"),
            () => cmd[3].ShouldBe("-p"),
            () => cmd[4].ShouldBe(CreditProbeSpec.DefaultPrompt));
    }

    [Fact]
    public async Task WhenStarted_DoesNotUseSh()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert — no shell wrapper; shell would interpret prompt content
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<string> cmd = captured.Cmd.ShouldNotBeNull();
        cmd.ShouldNotContain("sh");
        cmd.ShouldNotContain("/bin/sh");
    }

    [Fact]
    public async Task WhenLogOutputExceedsCap_TruncatesAtCap()
    {
        // Arrange — flood the log stream with lines that exceed the 65536-byte cap
        const int capBytes = 65_536;
        string longLine = new('x', 1024);
        // 100 lines × 1024 chars = 102400 bytes — well above the cap
        string[] floodLines = Enumerable.Repeat(longLine, 100).ToArray();
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines(floodLines);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<string> result = await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert — accumulated log must not exceed the cap
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.Length.ShouldBeLessThanOrEqualTo(capBytes);
    }

    [Fact]
    public async Task WhenLogLinesStradleCapBoundary_NeverThrowsAndStaysWithinCap()
    {
        // Arrange — craft lines so logs.Length lands at 65534, 65535, and 65536 across iterations,
        // straddling the cap boundary to expose the off-by-one that makes remaining negative.
        // Line 1: 65534 chars — fills the buffer to 65534 (no preceding newline on first line)
        // Line 2: 1 char — after the separator newline lands at 65536, remaining would be 0
        // Line 3: any char — a buggy cap check lets this iteration run AppendLine past the cap
        const int capBytes = 65_536;
        string[] straddle =
        [
            new string('a', 65534),
            "b",
            "c",
            "d",
        ];
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines(straddle);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act — must not throw ArgumentOutOfRangeException
        Result<string> result = await sut.RunCreditProbeAsync(OAuthSpec(), CancellationToken.None);

        // Assert — success with length within cap
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.Length.ShouldBeLessThanOrEqualTo(capBytes);
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
