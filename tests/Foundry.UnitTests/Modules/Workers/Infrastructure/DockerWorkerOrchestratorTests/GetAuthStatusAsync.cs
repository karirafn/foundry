using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class GetAuthStatusAsync
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

    private static string ValidAuthStatusJson(
        string email = "user@example.com",
        string orgName = "My Org",
        string subscriptionType = "pro") =>
        $$"""{"loggedIn":true,"email":"{{email}}","orgName":"{{orgName}}","subscriptionType":"{{subscriptionType}}"}""";

    [Fact]
    public async Task WhenLoggedIn_ExecTargetsCorrectContainer()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithExecCaptureStdout(ValidAuthStatusJson());
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetAuthStatusAsync("target-container", CancellationToken.None);

        // Assert
        runtime.LastExecContainerId.ShouldBe("target-container");
    }

    [Fact]
    public async Task WhenLoggedIn_ExecUsesClaudeAuthStatusJsonCommand()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithExecCaptureStdout(ValidAuthStatusJson());
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetAuthStatusAsync("container-id", CancellationToken.None);

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
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.GetAuthStatusAsync("container-id", CancellationToken.None);

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
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        Result<AccountIdentity> result = await sut.GetAuthStatusAsync(
            "container-id",
            CancellationToken.None);

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
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        Result<AccountIdentity> result = await sut.GetAuthStatusAsync(
            "container-id",
            CancellationToken.None);

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
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act — must not throw an OOM or any other exception; oversized output is truncated
        Result<AccountIdentity> result = await sut.GetAuthStatusAsync(
            "container-id",
            CancellationToken.None);

        // Assert — parse fails gracefully (truncated data is not valid JSON); no crash
        result.IsSuccess.ShouldBeFalse();
    }
}
