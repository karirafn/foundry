using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class ExecAsync
{
    private static DockerContainerRuntime BuildSut(FakeExecOperations execOps) =>
        new(new SpyContainerOperations(), new NullVolumeOperations(), execOps);

    [Fact]
    public async Task WhenCalled_PassesCommandToExecCreate()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);
        IReadOnlyList<string> command = ["sh", "-c", "echo hello"];

        // Act
        await sut.ExecAsync("container-id", command, [], CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.Cmd.ShouldBe(command);
    }

    [Fact]
    public async Task WhenCalled_PassesEnvironmentToExecCreate()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);
        IReadOnlyList<string> env = ["C=my-code"];

        // Act
        await sut.ExecAsync("container-id", ["sh", "-c", "cmd"], env, CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        IList<string> capturedEnv = captured.Env.ShouldNotBeNull();
        capturedEnv.ShouldContain("C=my-code");
    }

    [Fact]
    public async Task WhenCalled_SetsAttachStdoutAndStderrTrue()
    {
        // Arrange — must attach both stdout and stderr (matching DeliverLoginCodeAsync behavior)
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.ExecAsync("container-id", ["cmd"], [], CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.ShouldSatisfyAllConditions(
            () => captured.AttachStdout.ShouldBeTrue(),
            () => captured.AttachStderr.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenCalled_SetsAttachStdinFalse()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.ExecAsync("container-id", ["cmd"], [], CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.AttachStdin.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenCalled_TargetsCorrectContainer()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.ExecAsync("target-container", ["cmd"], [], CancellationToken.None);

        // Assert
        fake.LastContainerId.ShouldBe("target-container");
    }
}
