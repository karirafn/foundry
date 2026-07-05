using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class InspectAsync
{
    private static DockerContainerRuntime BuildSut(SpyContainerOperations containerOps) =>
        new(containerOps, new NullVolumeOperations(), new NullExecOperations());

    [Fact]
    public async Task WhenContainerExists_ReturnsInspectResponse()
    {
        // Arrange
        ContainerInspectResponse expected = new()
        {
            State = new ContainerState { Running = true },
        };
        SpyContainerOperations spy = new SpyContainerOperations().WithInspectResponse(expected);
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        ContainerInspectResponse result = await sut.InspectAsync("container-id", CancellationToken.None);

        // Assert
        result.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task WhenContainerNotFound_PropagatesDockerContainerNotFoundException()
    {
        // Arrange
        DockerContainerNotFoundException exception = SpyContainerOperations.ContainerNotFoundException();
        SpyContainerOperations spy = new SpyContainerOperations().WithInspectThrowing(exception);
        DockerContainerRuntime sut = BuildSut(spy);

        // Act & Assert — runtime does NOT swallow; caller is responsible for error classification
        await Should.ThrowAsync<DockerContainerNotFoundException>(
            async () => await sut.InspectAsync("gone-container", CancellationToken.None));
    }

    [Fact]
    public async Task WhenDockerApiException_PropagatesRawException()
    {
        // Arrange
        DockerApiException exception = new(HttpStatusCode.InternalServerError, "daemon error");
        SpyContainerOperations spy = new SpyContainerOperations().WithInspectThrowing(exception);
        DockerContainerRuntime sut = BuildSut(spy);

        // Act & Assert — raw exceptions surface to the caller
        await Should.ThrowAsync<DockerApiException>(
            async () => await sut.InspectAsync("container-id", CancellationToken.None));
    }
}
