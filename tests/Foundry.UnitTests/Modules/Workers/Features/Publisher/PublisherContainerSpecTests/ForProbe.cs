using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Publisher;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PublisherContainerSpecTests;

public sealed class ForProbe
{
    private static readonly HostProbeRequest Request = new(new Uri("https://gitlab.com/owner/repo.git"), "glpat_token");

    [Fact]
    public void RunsThePublisherImageInProbeModeWithTheCloneUrlAndToken()
    {
        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForProbe(Request);

        // Assert
        spec.ShouldSatisfyAllConditions(
            () => spec.Image.ShouldBe(WorkerImageNames.PublisherImageName),
            () => spec.Command.ShouldBe(["probe"]),
            () => spec.EnvironmentVariables["CLONE_URL"].ShouldBe("https://gitlab.com/owner/repo.git"),
            () => spec.EnvironmentVariables["GIT_PAT"].ShouldBe("glpat_token"));
    }

    [Fact]
    public void IsHardenedLikePrepare()
    {
        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForProbe(Request);

        // Assert
        spec.ShouldSatisfyAllConditions(
            () => spec.Capabilities.ShouldBeEmpty(),
            () => spec.DroppedCapabilities.ShouldContain("ALL"),
            () => spec.ReadOnlyRootFilesystem.ShouldBeTrue(),
            () => spec.ScratchVolumePaths.ShouldBe(WorkerHardeningSpec.PublisherScratchVolumePaths));
    }

    [Fact]
    public void MountsNoHandOffVolumeAndIsReapableAsTransient()
    {
        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForProbe(Request);

        // Assert
        spec.ShouldSatisfyAllConditions(
            () => spec.VolumeMounts.ShouldBeEmpty(),
            () => spec.Labels[PublisherContainerSpec.TransientLabelKey].ShouldBe("true"));
    }
}
