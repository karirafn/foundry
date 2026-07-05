using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class CreateVolumeAsync
{
    private static DockerContainerRuntime BuildSut(SpyVolumeOperations volumeOps) =>
        new(new SpyContainerOperations(), volumeOps, new NullExecOperations());

    [Fact]
    public async Task WhenCalled_PassesParametersToDockerApi()
    {
        // Arrange
        SpyVolumeOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);
        VolumesCreateParameters parameters = new()
        {
            Name = "foundry-test-volume",
            Labels = new Dictionary<string, string> { ["foundry.managed"] = "true" },
        };

        // Act
        await sut.CreateVolumeAsync(parameters, CancellationToken.None);

        // Assert
        VolumesCreateParameters captured = spy.LastCreateParameters.ShouldNotBeNull();
        captured.ShouldBeSameAs(parameters);
    }

    [Fact]
    public async Task WhenCalled_InvokesVolumeCreateOnDockerApi()
    {
        // Arrange
        SpyVolumeOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);
        VolumesCreateParameters parameters = new() { Name = "my-volume" };

        // Act
        await sut.CreateVolumeAsync(parameters, CancellationToken.None);

        // Assert
        spy.LastCreateParameters.ShouldNotBeNull();
    }
}
