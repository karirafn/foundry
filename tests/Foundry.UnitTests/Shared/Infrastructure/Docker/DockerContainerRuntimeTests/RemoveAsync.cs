using System.Net;

using Docker.DotNet;
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

        // Act
        Task act = sut.RemoveAsync("gone-container", CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task WhenAutoRemoveAlreadyInProgress_SwallowsConflictException()
    {
        // Arrange
        DockerApiException conflict = new(
            HttpStatusCode.Conflict,
            """{"message":"removal of container abc123 is already in progress"}""");
        SpyContainerOperations spy = new SpyContainerOperations().WithRemoveThrowing(conflict);
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        Task act = sut.RemoveAsync("abc123", CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task WhenUnrelatedConflictException_PropagatesDockerApiException()
    {
        // Arrange
        DockerApiException conflict = new(HttpStatusCode.Conflict, """{"message":"name already in use"}""");
        SpyContainerOperations spy = new SpyContainerOperations().WithRemoveThrowing(conflict);
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        Task act = sut.RemoveAsync("container-id", CancellationToken.None);

        // Assert
        await Should.ThrowAsync<DockerApiException>(act);
    }

    [Fact]
    public async Task WhenInternalServerError_PropagatesDockerApiException()
    {
        // Arrange
        DockerApiException serverError = new(HttpStatusCode.InternalServerError, """{"message":"daemon error"}""");
        SpyContainerOperations spy = new SpyContainerOperations().WithRemoveThrowing(serverError);
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        Task act = sut.RemoveAsync("container-id", CancellationToken.None);

        // Assert
        await Should.ThrowAsync<DockerApiException>(act);
    }
}
