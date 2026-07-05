using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class ExecCaptureStdoutAsync
{
    private static DockerContainerRuntime BuildSut(FakeExecOperations execOps) =>
        new(new SpyContainerOperations(), new NullVolumeOperations(), execOps);

    [Fact]
    public async Task WhenCalled_SetsCommandInExecCreateParameters()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);
        IReadOnlyList<string> command = ["claude", "auth", "status", "--json"];

        // Act
        await sut.ExecCaptureStdoutAsync("container-id", command, [], maxBytes: null, CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.Cmd.ShouldBe(command);
    }

    [Fact]
    public async Task WhenCalled_SetsAttachStdoutTrueAndAttachStderrFalse()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.ExecCaptureStdoutAsync("container-id", ["cmd"], [], maxBytes: null, CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.ShouldSatisfyAllConditions(
            () => captured.AttachStdout.ShouldBeTrue(),
            () => captured.AttachStderr.ShouldBeFalse(),
            () => captured.AttachStdin.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenEnvironmentIsNonEmpty_SetsEnvInExecCreateParameters()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);
        IReadOnlyList<string> environment = ["C=mycode", "FOO=bar"];

        // Act
        await sut.ExecCaptureStdoutAsync("container-id", ["cmd"], environment, maxBytes: null, CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.Env.ShouldBe(environment);
    }

    [Fact]
    public async Task WhenEnvironmentIsEmpty_LeavesEnvNullInExecCreateParameters()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.ExecCaptureStdoutAsync("container-id", ["cmd"], [], maxBytes: null, CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.Env.ShouldBeNull();
    }

    [Fact]
    public async Task WhenMaxBytesIsNull_ReturnsCapturedStdoutInFull()
    {
        // Arrange
        string stdout = "hello from container";
        FakeExecOperations fake = new(stdout);
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        string result = await sut.ExecCaptureStdoutAsync(
            "container-id", ["cmd"], [], maxBytes: null, CancellationToken.None);

        // Assert
        result.ShouldBe(stdout);
    }

    [Fact]
    public async Task WhenMaxBytesIsSet_BoundsReadToMaxBytes()
    {
        // Arrange — stdout is 20 chars, limit is 5
        FakeExecOperations fake = new("hello world, long output");
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        string result = await sut.ExecCaptureStdoutAsync(
            "container-id", ["cmd"], [], maxBytes: 5, CancellationToken.None);

        // Assert
        result.ShouldBe("hello");
        result.Length.ShouldBe(5);
    }

    [Fact]
    public async Task WhenCalled_PassesContainerIdToExecCreate()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.ExecCaptureStdoutAsync("my-container", ["cmd"], [], maxBytes: null, CancellationToken.None);

        // Assert
        fake.LastContainerId.ShouldBe("my-container");
    }
}
