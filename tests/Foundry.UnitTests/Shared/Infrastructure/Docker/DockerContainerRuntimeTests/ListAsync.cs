using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class ListAsync
{
    private static DockerContainerRuntime BuildSut(SpyContainerOperations containerOps) =>
        new(containerOps, new NullVolumeOperations(), new NullExecOperations());

    [Fact]
    public async Task WhenCalled_ReturnsContainersFromDockerApi()
    {
        // Arrange
        IList<ContainerListResponse> containers =
        [
            new ContainerListResponse { ID = "container-1" },
            new ContainerListResponse { ID = "container-2" },
        ];
        SpyContainerOperations spy = new SpyContainerOperations().WithListResponse(containers);
        DockerContainerRuntime sut = BuildSut(spy);
        ContainersListParameters parameters = new() { All = true };

        // Act
        IList<ContainerListResponse> result = await sut.ListAsync(parameters, CancellationToken.None);

        // Assert
        result.ShouldBe(containers);
    }

    [Fact]
    public async Task WhenCalled_PassesParametersToDockerApi()
    {
        // Arrange
        SpyContainerOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);
        ContainersListParameters parameters = new()
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { ["foundry.managed"] = true },
            },
        };

        // Act
        await sut.ListAsync(parameters, CancellationToken.None);

        // Assert
        spy.LastListParameters.ShouldBeSameAs(parameters);
    }
}
