using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerImageToolchainProbeTests;

public sealed class ProbeAsync
{
    private const string ImageId = "sha256:0123456789abcdef";

    private const string ToolchainLine = """FOUNDRY_TOOLCHAIN [{"name":"csharp-ls","version":"0.28.0"},{"name":"docker","version":"29.9.3"}]""";

    private static DockerWorkerImageToolchainProbe BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime, NullLogger<DockerWorkerImageToolchainProbe>.Instance);

    private static FakeDockerContainerRuntime Exiting(long exitCode, string? log) =>
        new FakeDockerContainerRuntime()
            .WithContainerId("toolchain-probe-1")
            .WithWaitExitCode(exitCode)
            .WithGetAllLogsResult(log);

    [Fact]
    public async Task WhenTheScriptReportsItsToolchain_ReturnsIt()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"2026-11-15T09:00:00.000000000Z {ToolchainLine}\n");

        // Act
        Toolchain? toolchain = await BuildSut(runtime).ProbeAsync(ImageId, CancellationToken.None);

        // Assert
        toolchain.ShouldNotBeNull().Entries.Select(e => e.Name).ShouldBe(["csharp-ls", "docker"]);
    }

    [Fact]
    public async Task WhenTheImageLacksTheScript_ReturnsNull()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(127, "exec: \"/opt/foundry/toolchain.sh\": stat /opt/foundry/toolchain.sh: no such file or directory\n");

        // Act
        Toolchain? toolchain = await BuildSut(runtime).ProbeAsync(ImageId, CancellationToken.None);

        // Assert
        toolchain.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheScriptExitsNonZero_ReturnsNull()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(1, $"{ToolchainLine}\n");

        // Act
        Toolchain? toolchain = await BuildSut(runtime).ProbeAsync(ImageId, CancellationToken.None);

        // Assert
        toolchain.ShouldBeNull();
    }

    [Fact]
    public async Task RunsTheImageByIdWithTheScriptAsEntrypointAsNodeWithNoEnvironment()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"{ToolchainLine}\n");

        // Act
        await BuildSut(runtime).ProbeAsync(ImageId, CancellationToken.None);

        // Assert
        CreateContainerParameters create = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        create.ShouldSatisfyAllConditions(
            () => create.Image.ShouldBe(ImageId),
            () => create.Entrypoint.ShouldBe(["/opt/foundry/toolchain.sh"]),
            () => create.Cmd.ShouldBeEmpty(),
            () => create.User.ShouldBe("node"),
            () => create.Env.ShouldBeEmpty(),
            () => create.Labels["foundry.transient"].ShouldBe("true"));
    }

    [Fact]
    public async Task RunsUnderTheWorkerHardeningWithNoCapabilityAddedBack()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"{ToolchainLine}\n");

        // Act
        await BuildSut(runtime).ProbeAsync(ImageId, CancellationToken.None);

        // Assert
        HostConfig host = runtime.LastCreateAndStartParameters.ShouldNotBeNull().HostConfig;
        host.ShouldSatisfyAllConditions(
            () => host.CapDrop.ShouldBe(["ALL"]),
            () => host.CapAdd.ShouldBeNull(),
            () => host.SecurityOpt.ShouldBe(["no-new-privileges:true"]),
            () => host.ReadonlyRootfs.ShouldBeTrue());
    }

    [Fact]
    public async Task AlwaysRemovesTheContainerAfterReadingItsLogs()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"{ToolchainLine}\n");

        // Act
        await BuildSut(runtime).ProbeAsync(ImageId, CancellationToken.None);

        // Assert
        runtime.LastGetAllLogsContainerId.ShouldBe("toolchain-probe-1");
        runtime.CallLog.ShouldBe(["wait:toolchain-probe-1", "remove:toolchain-probe-1"]);
    }

    [Fact]
    public async Task WhenDockerRefusesToStartIt_ReturnsNull()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithCreateAndStartThrowing(new DockerApiException(HttpStatusCode.NotFound, "No such image: sha256:0123456789abcdef"));

        // Act
        Toolchain? toolchain = await BuildSut(runtime).ProbeAsync(ImageId, CancellationToken.None);

        // Assert
        toolchain.ShouldBeNull();
        runtime.LastRemoveContainerId.ShouldBeNull();
    }
}
