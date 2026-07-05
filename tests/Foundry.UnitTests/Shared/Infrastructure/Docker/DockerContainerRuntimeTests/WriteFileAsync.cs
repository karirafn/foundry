using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class WriteFileAsync
{
    private static DockerContainerRuntime BuildSut(FakeExecOperations execOps) =>
        new(new SpyContainerOperations(), new NullVolumeOperations(), execOps);

    [Fact]
    public async Task WhenCalled_ExecsWithPrintfCommandAndFilePath()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.WriteFileAsync("container-id", "/etc/config.json", "content", CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.Cmd.ShouldBe(["sh", "-c", "printf '%s' \"$J\" > /etc/config.json"]);
    }

    [Fact]
    public async Task WhenCalled_PassesContentViaEnvJVariable()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);
        const string content = "{\"key\":\"value\"}";

        // Act
        await sut.WriteFileAsync("container-id", "/etc/config.json", content, CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.Env.ShouldContain($"J={content}");
    }

    [Fact]
    public async Task WhenCalled_SetsAttachStdoutFalseAndAttachStderrFalse()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.WriteFileAsync("container-id", "/etc/file.txt", "data", CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.ShouldSatisfyAllConditions(
            () => captured.AttachStdout.ShouldBeFalse(),
            () => captured.AttachStderr.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenCalled_PassesContainerIdToExecCreate()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.WriteFileAsync("my-container", "/tmp/file.txt", "data", CancellationToken.None);

        // Assert
        fake.LastContainerId.ShouldBe("my-container");
    }
}
