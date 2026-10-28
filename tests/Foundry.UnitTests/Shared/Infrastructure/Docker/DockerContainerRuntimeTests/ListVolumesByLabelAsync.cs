using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerContainerRuntimeTests;
#pragma warning disable CA1707 // underscores in test names

public sealed class ListVolumesByLabelAsync
{
    private static DockerContainerRuntime BuildSut(SpyVolumeOperations volumeOps) =>
        new(new SpyContainerOperations(), volumeOps, new NullExecOperations());

    [Fact]
    public async Task WhenCalled_PassesLabelFilterToDockerApi()
    {
        // Arrange
        SpyVolumeOperations spy = new();
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        await sut.ListVolumesByLabelAsync("foundry.data-root", CancellationToken.None);

        // Assert
        VolumesListParameters captured = spy.LastListParameters.ShouldNotBeNull();
        captured.Filters.ShouldNotBeNull();
        IDictionary<string, bool> labelFilter = captured.Filters["label"];
        labelFilter.ShouldContainKey("foundry.data-root");
        labelFilter["foundry.data-root"].ShouldBeTrue();
    }

    [Fact]
    public async Task WhenVolumesReturned_ProjectsToNameAndLabelsTuples()
    {
        // Arrange
        SpyVolumeOperations spy = new SpyVolumeOperations().WithListResponse(
            new VolumesListResponse
            {
                Volumes =
                [
                    new VolumeResponse
                    {
                        Name = "foundry-data-root-42",
                        Labels = new Dictionary<string, string>
                        {
                            ["foundry.worker-run-id"] = "42",
                            ["foundry.managed"] = "true",
                        },
                    },
                ],
            });
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        IReadOnlyList<(string Name, IReadOnlyDictionary<string, string> Labels)> result =
            await sut.ListVolumesByLabelAsync("foundry.worker-run-id", CancellationToken.None);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("foundry-data-root-42");
        result[0].Labels["foundry.worker-run-id"].ShouldBe("42");
        result[0].Labels["foundry.managed"].ShouldBe("true");
    }

    [Fact]
    public async Task WhenVolumesIsNull_ReturnsEmptyList()
    {
        // Arrange
        SpyVolumeOperations spy = new SpyVolumeOperations().WithListResponse(
            new VolumesListResponse { Volumes = null });
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        IReadOnlyList<(string Name, IReadOnlyDictionary<string, string> Labels)> result =
            await sut.ListVolumesByLabelAsync("foundry.data-root", CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenVolumesIsEmpty_ReturnsEmptyList()
    {
        // Arrange
        SpyVolumeOperations spy = new SpyVolumeOperations().WithListResponse(
            new VolumesListResponse { Volumes = [] });
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        IReadOnlyList<(string Name, IReadOnlyDictionary<string, string> Labels)> result =
            await sut.ListVolumesByLabelAsync("foundry.data-root", CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenVolumeHasNullLabels_CoalescesToEmptyDictionary()
    {
        // Arrange
        SpyVolumeOperations spy = new SpyVolumeOperations().WithListResponse(
            new VolumesListResponse
            {
                Volumes =
                [
                    new VolumeResponse { Name = "no-labels-volume", Labels = null },
                ],
            });
        DockerContainerRuntime sut = BuildSut(spy);

        // Act
        IReadOnlyList<(string Name, IReadOnlyDictionary<string, string> Labels)> result =
            await sut.ListVolumesByLabelAsync("foundry.data-root", CancellationToken.None);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("no-labels-volume");
        result[0].Labels.ShouldBeEmpty();
    }
}
