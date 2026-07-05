using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class DeliverLoginCodeAsync
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

    [Fact]
    public async Task WhenCodeDelivered_ExecCreatedInCorrectContainer()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.DeliverLoginCodeAsync("test-container-id", "my-code", CancellationToken.None);

        // Assert
        runtime.LastExecAsyncContainerId.ShouldBe("test-container-id");
    }

    [Fact]
    public async Task WhenCodeDelivered_ExecArgvMatchesForCodeCommand()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.DeliverLoginCodeAsync("container-id", "auth-code-abc", CancellationToken.None);

        // Assert — argv uses env-var delivery (ForCode), not stdin (ForStdin)
        LoginExecCommand expectedCmd = LoginExecCommand.ForCode();
        IReadOnlyList<string> captured = runtime.LastExecAsyncCommand.ShouldNotBeNull();
        captured.ShouldBe(expectedCmd.Argv);
    }

    [Fact]
    public async Task WhenCodeDelivered_CodeIsInEnvVarNotArgv()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.DeliverLoginCodeAsync("container-id", "secret-code", CancellationToken.None);

        // Assert — code carried as env var C=<code>, never interpolated into argv
        IReadOnlyList<string> captured = runtime.LastExecAsyncCommand.ShouldNotBeNull();
        string allArgs = string.Join(" ", captured);
        allArgs.ShouldNotContain("secret-code");

        IReadOnlyList<string> env = runtime.LastExecAsyncEnvironment.ShouldNotBeNull();
        env.ShouldContain("C=secret-code");
    }

    [Fact]
    public async Task WhenCodeContainsShellMetacharacters_CodeStaysLiteralInEnv()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);
        const string codeWithMeta = @"abc$'"";&|";

        // Act
        await sut.DeliverLoginCodeAsync("container-id", codeWithMeta, CancellationToken.None);

        // Assert — the literal code value is in the env var, not shell-expanded
        IReadOnlyList<string> env = runtime.LastExecAsyncEnvironment.ShouldNotBeNull();
        env.ShouldContain($"C={codeWithMeta}");

        // The argv must not contain the metacharacters literally (they stay in the env var)
        IReadOnlyList<string> captured = runtime.LastExecAsyncCommand.ShouldNotBeNull();
        string allArgs = string.Join(" ", captured);
        allArgs.ShouldNotContain(codeWithMeta);
    }

    [Fact]
    public async Task WhenCodeDelivered_NoStreamWriteOccurs()
    {
        // Arrange — ExecAsync discards all output; no writes to a capturing stream
        FakeDockerContainerRuntime runtime = new();
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.DeliverLoginCodeAsync("container-id", "secret-code", CancellationToken.None);

        // Assert — ExecAsync was called (not ExecCaptureStdoutAsync which would read stdout)
        runtime.LastExecAsyncContainerId.ShouldNotBeNull();
        runtime.LastExecContainerId.ShouldBeNull();
    }
}
