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

    private static readonly string DefaultTimeoutStr =
        UsageReportSpec.DefaultTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
    public async Task WhenLogLinesCarryDockerTimestamps_StripsPrefixSoParserReturnsParsed()
    {
        // Arrange — the real framing StreamLogsAsync delivers (Timestamps = true)
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithStreamLogLines(
                """2026-11-03T08:36:12.901234567Z {"type":"system","subtype":"init","cwd":"/workspace","session_id":"abc123","tools":[],"mcp_servers":[],"model":"claude-opus-4-8","permissionMode":"default","apiKeySource":"none"}""",
                """2026-11-03T08:36:13.998877665Z {"type":"assistant","message":{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3}},"parent_tool_use_id":null,"session_id":"abc123"}""",
                """2026-11-03T08:36:14.124595895Z {"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1793399400,"rateLimitType":"five_hour","overageStatus":"allowed","isUsingOverage":false,"unifiedWindows":{"five_hour":{"utilization":0.12,"resetsAt":1793399400},"seven_day":{"utilization":0.34,"resetsAt":1793527200}}},"uuid":"dab406c8-9ba0-40d4-b473-fcbdabd857e3","session_id":"abc123"}""",
                """2026-11-03T08:36:14.250000000Z {"type":"result","subtype":"success","is_error":false,"duration_ms":1350,"duration_api_ms":1000,"num_turns":1,"result":"ok","total_cost_usd":0.001,"usage":{"input_tokens":10,"output_tokens":3},"session_id":"abc123"}""");
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<string> result = await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert — prefix-free lines that the usage parser accepts
        string log = result.ShouldBeOfType<Result<string>.Success>().Value;
        log.ShouldNotContain("2026-11-03T08:36:");
        UsageParseResult parsed = UsageReportParser.Parse(log, "user@example.com", DateTimeOffset.UnixEpoch);
        UsageParseResult.Parsed success = parsed.ShouldBeOfType<UsageParseResult.Parsed>();
        success.Snapshot.Limits.ShouldContain(l => l.Kind == "session");
        success.Snapshot.Limits.ShouldContain(l => l.Kind == "weekly_all");
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
        // ["timeout", "<seconds>", "claude", "-p", "<prompt>", "--max-turns", "1",
        //  "--output-format", "stream-json", "--verbose"]
        // --max-turns 1 limits the turn to one exchange so a rate_limit_event is reliably emitted
        // after the assistant message, without risk of recursive tool use.
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<string> cmd = captured.Cmd.ShouldNotBeNull();
        cmd.ShouldSatisfyAllConditions(
            () => cmd[0].ShouldBe("timeout"),
            () => cmd[1].ShouldBe(DefaultTimeoutStr),
            () => cmd[2].ShouldBe("claude"),
            () => cmd[3].ShouldBe("-p"),
            () => cmd[4].ShouldBe(UsageReportSpec.UsagePrompt),
            () => cmd[5].ShouldBe("--max-turns"),
            () => cmd[6].ShouldBe("1"),
            () => cmd[7].ShouldBe("--output-format"),
            () => cmd[8].ShouldBe("stream-json"),
            () => cmd[9].ShouldBe("--verbose"));
    }

    [Fact]
    public async Task WhenStarted_DoesNotUseSh()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.RunUsageReportAsync(DefaultSpec(), CancellationToken.None);

        // Assert — no shell wrapper; shell would interpret prompt flags or slash commands
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
