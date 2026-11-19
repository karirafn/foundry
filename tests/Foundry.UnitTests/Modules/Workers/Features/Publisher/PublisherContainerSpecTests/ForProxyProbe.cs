using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Publisher;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PublisherContainerSpecTests;

public sealed class ForProxyProbe
{
    private static readonly Uri ProxyUrl = new("http://host.docker.internal:5287/package-sources/");
    private const string ExtraHost = "host.docker.internal:172.28.0.1";

    [Fact]
    public void RunsThePublisherImageInProxyProbeModeWithTheProxyUrlAndNoToken()
    {
        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForProxyProbe(ProxyUrl, ExtraHost);

        // Assert
        spec.ShouldSatisfyAllConditions(
            () => spec.Image.ShouldBe(WorkerImageNames.PublisherImageName),
            () => spec.Command.ShouldBe(["proxy-probe"]),
            () => spec.EnvironmentVariables.ShouldBe(
                new Dictionary<string, string> { ["PROXY_URL"] = "http://host.docker.internal:5287/package-sources/" }));
    }

    [Fact]
    public void CarriesTheWorkersExtraHostEntry()
    {
        PublisherContainerSpec.ForProxyProbe(ProxyUrl, ExtraHost).ExtraHosts.ShouldBe([ExtraHost]);
    }

    [Fact]
    public void IsHardenedLikeTheHostProbe()
    {
        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForProxyProbe(ProxyUrl, ExtraHost);
        WorkerContainerSpec hostProbe = PublisherContainerSpec.ForProbe(
            new HostProbeRequest(new Uri("https://gitlab.com/owner/repo.git"), "token"));

        // Assert
        spec.ShouldSatisfyAllConditions(
            () => spec.Capabilities.ShouldBeEmpty(),
            () => spec.DroppedCapabilities.ShouldContain("ALL"),
            () => spec.ReadOnlyRootFilesystem.ShouldBeTrue(),
            () => spec.ScratchVolumePaths.ShouldBe(WorkerHardeningSpec.PublisherScratchVolumePaths),
            () => spec.MemoryLimitMb.ShouldBe(hostProbe.MemoryLimitMb),
            () => spec.CpuLimit.ShouldBe(hostProbe.CpuLimit),
            () => spec.PidsLimit.ShouldBe(hostProbe.PidsLimit),
            () => spec.VolumeMounts.ShouldBeEmpty(),
            () => spec.Labels[PublisherContainerSpec.TransientLabelKey].ShouldBe("true"));
    }
}
