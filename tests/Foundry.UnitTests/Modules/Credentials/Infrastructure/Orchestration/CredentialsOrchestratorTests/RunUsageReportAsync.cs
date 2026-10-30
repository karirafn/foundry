using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.Orchestration.CredentialsOrchestratorTests;

public sealed class RunUsageReportAsync
{
    private static CredentialsOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime);

    private static UsageReportSpec DefaultSpec() =>
        new(TimeoutSeconds: UsageReportSpec.DefaultTimeoutSeconds);

    [Fact]
    public async Task WhenContainerRunsSuccessfully_ReturnsOkWithLogs()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines("stream-json output line 1", "stream-json output line 2");
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<string> result = await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldContain("stream-json output line 1");
    }

    [Fact]
    public async Task WhenStarted_UsesLoginImageName()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

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
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Labels.ShouldContainKey("foundry.transient");
        captured.Labels["foundry.transient"].ShouldBe("true");
    }

    [Fact]
    public async Task WhenStarted_SetsRoleUsageLabel()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Labels.ShouldContainKey("foundry.role");
        captured.Labels["foundry.role"].ShouldBe("usage");
    }

    [Fact]
    public async Task WhenStarted_SetsAutoRemoveTrue()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.HostConfig.AutoRemove.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenStarted_CmdIsDirectArgvWithTimeoutAndUsageFlags()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert — argv form:
        // ["timeout", "<seconds>", "claude", "-p", "/usage", "--output-format", "stream-json", "--verbose"]
        // No shell interpretation of the prompt or flags.
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<string> cmd = captured.Cmd.ShouldNotBeNull();
        cmd.ShouldSatisfyAllConditions(
            () => cmd[0].ShouldBe("timeout"),
            () => cmd[1].ShouldBe(UsageReportSpec.DefaultTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            () => cmd[2].ShouldBe("claude"),
            () => cmd[3].ShouldBe("-p"),
            () => cmd[4].ShouldBe(UsageReportSpec.UsagePrompt),
            () => cmd[5].ShouldBe("--output-format"),
            () => cmd[6].ShouldBe("stream-json"),
            () => cmd[7].ShouldBe("--verbose"));
    }

    [Fact]
    public async Task WhenStarted_DoesNotUseSh()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert — no shell wrapper; shell would interpret the /usage prompt as a file path on some systems
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<string> cmd = captured.Cmd.ShouldNotBeNull();
        cmd.ShouldNotContain("sh");
        cmd.ShouldNotContain("/bin/sh");
    }

    [Fact]
    public async Task WhenStarted_MountsCredentialVolumeReadWrite()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert — read-write (ReadOnly = false): the CLI may refresh an expired access token
        // during the /usage call, and a read-only mount cannot persist the rotated refresh token.
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.ShouldContain(m =>
            m.Type == "volume"
            && m.Source == WorkerVolumeNames.CredentialVolumeName
            && m.Target == WorkerVolumeNames.ClaudeConfigContainerPath
            && !m.ReadOnly);
    }

    [Fact]
    public async Task WhenStarted_SetsClaudeConfigDirEnv()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        string expectedEnv = $"{WorkerVolumeNames.ClaudeConfigDirEnvVar}={WorkerVolumeNames.ClaudeConfigContainerPath}";
        captured.Env.ShouldContain(expectedEnv);
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
        Result<string> result = await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Code.ShouldBe("Docker.UsageReportStartFailed");
    }

    [Fact]
    public async Task WhenLogOutputExceedsCap_TruncatesAtCap()
    {
        // Arrange — flood the log stream with lines that exceed the 65536-byte cap
        const int capBytes = 65_536;
        string longLine = new('x', 1024);
        string[] floodLines = Enumerable.Repeat(longLine, 100).ToArray();
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines(floodLines);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<string> result = await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert — accumulated log must not exceed the cap
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.Length.ShouldBeLessThanOrEqualTo(capBytes);
    }
}
