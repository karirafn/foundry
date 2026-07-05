using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class CreateAndStartAsync
{
    private static DockerContainerRuntime BuildSut(
        SpyContainerOperations? containerOps = null) =>
        new(
            containerOps ?? new SpyContainerOperations(),
            new NullVolumeOperations(),
            new NullExecOperations());

    [Fact]
    public async Task WhenCalled_ReturnsContainerIdFromCreateResponse()
    {
        // Arrange
        SpyContainerOperations spy = new SpyContainerOperations().WithContainerId("created-abc");
        DockerContainerRuntime sut = BuildSut(spy);
        CreateContainerParameters createParams = new() { Image = "test-image" };

        // Act
        string containerId = await sut.CreateAndStartAsync(createParams, CancellationToken.None);

        // Assert
        containerId.ShouldBe("created-abc");
    }

    [Fact]
    public async Task WhenCalled_PassesCreateParametersToDockerApi()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);
        CreateContainerParameters createParams = new()
        {
            Image = "test-image:v1",
            Cmd = ["sh", "-c", "echo hello"],
        };

        // Act
        await sut.CreateAndStartAsync(createParams, CancellationToken.None);

        // Assert
        CreateContainerParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.ShouldBeSameAs(createParams);
    }

    [Fact]
    public async Task WhenCreateThrowsDockerApiException_PropagatesRawException()
    {
        // Arrange
        DockerApiException expected = new(HttpStatusCode.InternalServerError, "image not found");
        SpyContainerOperations spy = new SpyContainerOperations().WithCreateThrowing(expected);
        DockerContainerRuntime sut = BuildSut(spy);
        CreateContainerParameters createParams = new() { Image = "missing-image" };

        // Act & Assert
        DockerApiException thrown = await Should.ThrowAsync<DockerApiException>(
            async () => await sut.CreateAndStartAsync(createParams, CancellationToken.None));
        thrown.ShouldBeSameAs(expected);
    }
}
