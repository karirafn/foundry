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
    public async Task WhenFilePathContainsShellMetacharacters_PathDeliveredViaEnvVarNotInterpolated()
    {
        // Arrange — a path with shell metacharacters that would cause injection if interpolated directly
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);
        string dangerousPath = "/tmp/x; touch /pwned";

        // Act
        await sut.WriteFileAsync("container-id", dangerousPath, "content", CancellationToken.None);

        // Assert — shell command string must reference $P by name, not the raw path string
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        IList<string> cmd = captured.Cmd.ShouldNotBeNull();
        string shellCmd = cmd[2];
        shellCmd.ShouldContain("\"$P\"");
        shellCmd.ShouldNotContain(dangerousPath);

        // Assert — path delivered safely via the P env var
        IList<string> env = captured.Env.ShouldNotBeNull();
        env.ShouldContain($"P={dangerousPath}");
    }

    [Fact]
    public async Task WhenCalled_ExecsWithPrintfCommandUsingEnvVarReferences()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.WriteFileAsync("container-id", "/etc/config.json", "content", CancellationToken.None);

        // Assert — both content ($J) and path ($P) referenced via env vars, not interpolated
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        IList<string> cmd = captured.Cmd.ShouldNotBeNull();
        cmd[2].ShouldBe("printf '%s' \"$J\" > \"$P\"");
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
    public async Task WhenCalled_PassesFilePathViaEnvPVariable()
    {
        // Arrange
        FakeExecOperations fake = new();
        DockerContainerRuntime sut = BuildSut(fake);

        // Act
        await sut.WriteFileAsync("container-id", "/etc/config.json", "content", CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = fake.LastCreateParameters.ShouldNotBeNull();
        captured.Env.ShouldContain("P=/etc/config.json");
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
