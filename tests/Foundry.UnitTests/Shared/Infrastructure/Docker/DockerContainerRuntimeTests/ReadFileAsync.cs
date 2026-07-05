using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class ReadFileAsync
{
    private static DockerContainerRuntime BuildSut(FakeExecOperations execOps) =>
        new(new SpyContainerOperations(), new NullVolumeOperations(), execOps);

    [Fact]
    public async Task WhenFileExists_ReturnsFileContent()
    {
        // Arrange
        FakeExecOperations fake = new("file content here");
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        string? result = await sut.ReadFileAsync("container-id", "/etc/file.txt", CancellationToken.None);

        // Assert
        result.ShouldBe("file content here");
    }

    [Fact]
    public async Task WhenFileIsEmpty_ReturnsNull()
    {
        // Arrange
        FakeExecOperations fake = new("");
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        string? result = await sut.ReadFileAsync("container-id", "/etc/missing.txt", CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFileContainsOnlyWhitespace_ReturnsNull()
    {
        // Arrange
        FakeExecOperations fake = new("   \n\t  ");
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        string? result = await sut.ReadFileAsync("container-id", "/etc/blank.txt", CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenCalled_ExecsWithCatCommand()
    {
        // Arrange
        FakeExecOperations fake = new("content");
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.ReadFileAsync("container-id", "/etc/config.json", CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.Cmd.ShouldBe(["cat", "/etc/config.json"]);
    }

    [Fact]
    public async Task WhenCalled_SetsAttachStdoutTrueAndAttachStderrFalse()
    {
        // Arrange
        FakeExecOperations fake = new("content");
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.ReadFileAsync("container-id", "/etc/file.txt", CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.ShouldSatisfyAllConditions(
            () => captured.AttachStdout.ShouldBeTrue(),
            () => captured.AttachStderr.ShouldBeFalse());
    }
}
