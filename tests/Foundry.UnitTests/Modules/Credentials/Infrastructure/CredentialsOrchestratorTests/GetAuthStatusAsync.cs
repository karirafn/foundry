using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.CredentialsOrchestratorTests;

/// <summary>
/// Tests for credential volume auth status parsing. The internal <c>GetAuthStatusAsync</c>
/// helper is exercised through <see cref="CredentialsOrchestrator.GetCredentialVolumeAuthStatusAsync"/>,
/// which starts a helper container, execs the CLI, then tears it down.
/// </summary>
public sealed class GetAuthStatusAsync
{
    private static CredentialsOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime);

    private static string ValidAuthStatusJson(
        string email = "user@example.com",
        string orgName = "My Org",
        string subscriptionType = "pro") =>
        $$"""{"loggedIn":true,"email":"{{email}}","orgName":"{{orgName}}","subscriptionType":"{{subscriptionType}}"}""";

    [Fact]
    public async Task WhenLoggedIn_ExecUsesClaudeAuthStatusJsonCommand()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithExecCaptureStdout(ValidAuthStatusJson());
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert
        IReadOnlyList<string> captured = runtime.LastExecCommand.ShouldNotBeNull();
        captured.ShouldBe(["claude", "auth", "status", "--json"]);
    }

    [Fact]
    public async Task WhenLoggedIn_ExecAttachesStdout()
    {
        // Arrange — ExecCaptureStdoutAsync attaches stdout by design; this is covered at the runtime
        // layer. At the orchestrator level, verify it delegates to ExecCaptureStdoutAsync (not ExecAsync).
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithExecCaptureStdout(ValidAuthStatusJson());
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert — ExecCaptureStdoutAsync was invoked (LastExecContainerId set), not ExecAsync
        runtime.LastExecContainerId.ShouldNotBeNull();
        runtime.LastExecAsyncContainerId.ShouldBeNull();
    }

    [Fact]
    public async Task WhenLoggedIn_ReturnsAccountIdentityWithParsedFields()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithExecCaptureStdout(ValidAuthStatusJson("alice@acme.com", "Acme Corp", "max"));
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<AccountIdentity> result = await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        AccountIdentity identity = result.ShouldBeOfType<Result<AccountIdentity>.Success>().Value;
        identity.ShouldSatisfyAllConditions(
            () => identity.Email.ShouldBe("alice@acme.com"),
            () => identity.OrgName.ShouldBe("Acme Corp"),
            () => identity.SubscriptionType.ShouldBe("max"));
    }

    [Fact]
    public async Task WhenNotLoggedIn_ReturnsFailure()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithExecCaptureStdout("""{"loggedIn":false}""");
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        Result<AccountIdentity> result = await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ShouldBeOfType<Result<AccountIdentity>.Failure>().Error.Code.ShouldBe("AccountIdentity.NotLoggedIn");
    }

    [Fact]
    public async Task WhenOutputExceedsCap_DoesNotThrowOrConsumeUnboundedMemory()
    {
        // Arrange — produce output well beyond the 16 KB cap (20 KB of padding)
        string oversizedOutput = new('x', 20_480);
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithExecCaptureStdout(oversizedOutput);
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act — must not throw an OOM or any other exception; oversized output is truncated
        Result<AccountIdentity> result = await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert — parse fails gracefully (truncated data is not valid JSON); no crash
        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenComplete_StartsHelperContainerWithReadOnlyMount()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithExecCaptureStdout(ValidAuthStatusJson());
        CredentialsOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetCredentialVolumeAuthStatusAsync(CancellationToken.None);

        // Assert — helper container mounts credential volume read-only
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.ShouldContain(m =>
            m.Type == "volume"
            && m.Source == WorkerVolumeNames.CredentialVolumeName
            && m.Target == WorkerVolumeNames.ClaudeConfigContainerPath
            && m.ReadOnly);
    }
}
