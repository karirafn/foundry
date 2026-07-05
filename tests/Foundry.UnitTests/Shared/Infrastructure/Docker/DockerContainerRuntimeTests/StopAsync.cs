using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class StopAsync
{
    private static DockerContainerRuntime BuildSut(SpyContainerOperations containerOps) =>
        new(containerOps, new NullVolumeOperations(), new NullExecOperations());

    [Fact]
    public async Task WhenCalled_PassesWaitBeforeKillSecondsToDockerApi()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        await sut.StopAsync("container-id", waitBeforeKillSeconds: 15, CancellationToken.None);

        // Assert
        ContainerStopParameters captured = spy.LastStopParameters.ShouldNotBeNull();
        captured.WaitBeforeKillSeconds.ShouldBe(15u);
    }

    [Fact]
    public async Task WhenCalled_PassesContainerIdToDockerApi()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        await sut.StopAsync("my-container", waitBeforeKillSeconds: 10, CancellationToken.None);

        // Assert
        spy.LastStopContainerId.ShouldBe("my-container");
    }

    [Fact]
    public async Task WhenContainerAlreadyGone_SwallowsDockerContainerNotFoundException()
    {
        // Arrange
        SpyContainerOperations spy = new SpyContainerOperations()
            .WithStopThrowing(SpyContainerOperations.ContainerNotFoundException());
        DockerContainerRuntime sut = BuildSut(spy);

        // Act & Assert — must not throw
        await sut.StopAsync("gone-container", waitBeforeKillSeconds: 10, CancellationToken.None);
    }
}
