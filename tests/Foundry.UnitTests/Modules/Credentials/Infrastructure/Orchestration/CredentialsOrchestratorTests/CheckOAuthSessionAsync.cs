using System.Globalization;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Features.SessionCheck;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.Orchestration.CredentialsOrchestratorTests;

public sealed class CheckOAuthSessionAsync
{
    private const string CredentialsPath = "/home/node/.claude/.credentials.json";

    private static string Credentials(string refreshToken, DateTimeOffset expiresAt) =>
        $$$"""
        {"claudeAiOauth":{"accessToken":"sk-ant-oat01-x","refreshToken":"{{{refreshToken}}}","expiresAt":{{{expiresAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}}}}}
        """;

    private const string DeadCredentials =
        """{"claudeAiOauth":{"accessToken":"","refreshToken":"","expiresAt":0}}""";

    [Fact]
    public async Task WhenSessionActive_ReadsTheFileWithoutRunningTheCli()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithContainerId("helper")
            .WithReadFileResult(Credentials("sk-ant-ort01-x", DateTimeOffset.UtcNow.AddHours(1)));
        CredentialsOrchestrator sut = new(runtime);

        // Act
        Result<OAuthSessionStatus> result = await sut.CheckOAuthSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<OAuthSessionStatus>.Success>().Value
            .ShouldBeOfType<OAuthSessionStatus.Active>();
        runtime.CallLog.ShouldBe([$"read:{CredentialsPath}", "stop:helper", "remove:helper"]);
    }

    [Fact]
    public async Task WhenExpired_LetsTheCliRefreshThenReclassifies()
    {
        // Arrange — the CLI refresh is rejected and it clears the stored tokens.
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithContainerId("helper")
            .WithReadFileResults(
                Credentials("sk-ant-ort01-x", DateTimeOffset.UtcNow.AddMinutes(-1)),
                DeadCredentials);
        CredentialsOrchestrator sut = new(runtime);

        // Act
        Result<OAuthSessionStatus> result = await sut.CheckOAuthSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<OAuthSessionStatus>.Success>().Value
            .ShouldBeOfType<OAuthSessionStatus.RefreshRejected>();
        runtime.CallLog.ShouldBe(
        [
            $"read:{CredentialsPath}",
            "exec:timeout 20 claude auth status --json",
            $"read:{CredentialsPath}",
            "stop:helper",
            "remove:helper",
        ]);
    }

    [Fact]
    public async Task WhenExpiredAndCliRefreshSucceeds_ReturnsActive()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithReadFileResults(
                Credentials("sk-ant-ort01-old", DateTimeOffset.UtcNow.AddMinutes(-1)),
                Credentials("sk-ant-ort01-new", DateTimeOffset.UtcNow.AddHours(8)));
        CredentialsOrchestrator sut = new(runtime);

        // Act
        Result<OAuthSessionStatus> result = await sut.CheckOAuthSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<OAuthSessionStatus>.Success>().Value
            .ShouldBeOfType<OAuthSessionStatus.Active>();
    }

    [Fact]
    public async Task WhenCliRefreshFailsTransiently_ReturnsExpired()
    {
        // Arrange — a network error leaves the file untouched.
        string expired = Credentials("sk-ant-ort01-x", DateTimeOffset.UtcNow.AddMinutes(-1));
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithReadFileResults(expired, expired);
        CredentialsOrchestrator sut = new(runtime);

        // Act
        Result<OAuthSessionStatus> result = await sut.CheckOAuthSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<OAuthSessionStatus>.Success>().Value
            .ShouldBeOfType<OAuthSessionStatus.Expired>();
    }

    [Fact]
    public async Task WhenAlreadyRejectedByAWorker_DoesNotRunTheCli()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithContainerId("helper")
            .WithReadFileResult(DeadCredentials);
        CredentialsOrchestrator sut = new(runtime);

        // Act
        Result<OAuthSessionStatus> result = await sut.CheckOAuthSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<OAuthSessionStatus>.Success>().Value
            .ShouldBeOfType<OAuthSessionStatus.RefreshRejected>();
        runtime.CallLog.ShouldNotContain(entry => entry.StartsWith("exec:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HelperContainerMountsTheCredentialVolumeReadWrite()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithReadFileResult(DeadCredentials);
        CredentialsOrchestrator sut = new(runtime);

        // Act
        await sut.CheckOAuthSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        captured.Labels["foundry.transient"].ShouldBe("true");
        captured.HostConfig.Mounts.ShouldNotBeNull().ShouldContain(m =>
            m.Type == "volume"
            && m.Source == WorkerVolumeNames.CredentialVolumeName
            && m.Target == WorkerVolumeNames.ClaudeConfigContainerPath
            && !m.ReadOnly);
    }

    [Fact]
    public async Task WhenDockerFails_ReturnsFailure()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithCreateAndStartThrowing(new DockerApiException(System.Net.HttpStatusCode.InternalServerError, "daemon down"));
        CredentialsOrchestrator sut = new(runtime);

        // Act
        Result<OAuthSessionStatus> result = await sut.CheckOAuthSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<OAuthSessionStatus>.Failure>().Error.Code.ShouldBe("Docker.SessionCheckFailed");
    }
}
