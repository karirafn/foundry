using Foundry.Modules.Workers.Features.Publisher;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PackageProxyProbeOutputParserTests;

public sealed class Parse
{
    // The line shapes workers/publisher/publisher.sh proxy-probe prints.
    private const string ReachableLine = "FOUNDRY_PROXY_PROBE result=reachable detail=the proxy answered HTTP/1.1 404 Not Found";

    private const string UnreachableLine =
        "FOUNDRY_PROXY_PROBE result=unreachable detail=wget: can't connect to remote host (192.168.65.254): Connection refused";

    [Fact]
    public void WhenTheLineReportsReachable_ReturnsReachable()
    {
        PackageProxyProbeOutputParser.Parse($"Connecting to host.docker.internal:5287\n{ReachableLine}\n")
            .ShouldBe(new PackageProxyProbeOutcome.Reachable());
    }

    [Fact]
    public void WhenTheLineReportsUnreachable_ReturnsUnreachableWithItsDetail()
    {
        PackageProxyProbeOutputParser.Parse(UnreachableLine + "\n").ShouldBe(new PackageProxyProbeOutcome.Unreachable(
            "wget: can't connect to remote host (192.168.65.254): Connection refused"));
    }

    [Fact]
    public void WhenTheLineCarriesADockerTimestamp_ReadsPastIt()
    {
        PackageProxyProbeOutputParser.Parse($"2026-11-19T07:40:00.123456789Z {ReachableLine}\r\n")
            .ShouldBe(new PackageProxyProbeOutcome.Reachable());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PROXY_URL must be an http:// or https:// URL\n")]
    [InlineData("FOUNDRY_PROBE result=reachable detail=the provider answered ls-remote\n")]
    public void WhenNoResultLineIsPrinted_ReturnsInconclusive(string? log)
    {
        PackageProxyProbeOutputParser.Parse(log).ShouldBe(
            new PackageProxyProbeOutcome.Inconclusive(PackageProxyProbeOutputParser.MissingResultMessage));
    }

    [Fact]
    public void WhenTwoResultLinesArePrinted_ReturnsInconclusive()
    {
        PackageProxyProbeOutputParser.Parse($"{ReachableLine}\n{UnreachableLine}\n").ShouldBeOfType<PackageProxyProbeOutcome.Inconclusive>();
    }
}
