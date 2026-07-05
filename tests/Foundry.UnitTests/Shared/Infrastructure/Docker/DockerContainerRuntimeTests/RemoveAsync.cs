using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class RemoveAsync
{
    private static DockerContainerRuntime BuildSut(SpyContainerOperations containerOps) =>
        new(containerOps, new NullVolumeOperations(), new NullExecOperations());

    [Fact]
    public async Task WhenCalled_PassesContainerIdToDockerApi()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        await sut.RemoveAsync("my-container", CancellationToken.None);

        // Assert
        spy.LastRemoveContainerId.ShouldBe("my-container");
    }

    [Fact]
    public async Task WhenCalled_ForcesRemoval()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        await sut.RemoveAsync("container-id", CancellationToken.None);

        // Assert
        ContainerRemoveParameters captured = spy.LastRemoveParameters.ShouldNotBeNull();
        captured.Force.ShouldBe(true);
    }

    [Fact]
    public async Task WhenContainerAlreadyGone_SwallowsDockerContainerNotFoundException()
    {
        // Arrange
        SpyContainerOperations spy = new SpyContainerOperations()
            .WithRemoveThrowing(SpyContainerOperations.ContainerNotFoundException());
        DockerContainerRuntime sut = BuildSut(spy);

        // Act & Assert — must not throw
        await sut.RemoveAsync("gone-container", CancellationToken.None);
    }
}
