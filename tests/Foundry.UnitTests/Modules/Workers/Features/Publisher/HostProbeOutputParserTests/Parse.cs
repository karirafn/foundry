using Foundry.Modules.Workers.Features.Publisher;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.HostProbeOutputParserTests;

public sealed class Parse
{
    // The lines publisher.sh probe printed, run against https://foundry-probe.invalid/x and https://127.0.0.1:1/x with
    // git 2.39.5 on 2026-11-13 (workers/publisher/publisher.sh, probe mode).
    private const string DnsFailureLine =
        "FOUNDRY_PROBE result=unreachable detail=fatal: unable to access 'https://foundry-probe.invalid/x.git/': Could not resolve host: foundry-probe.invalid";

    private const string ReachableLine = "FOUNDRY_PROBE result=reachable detail=the provider answered ls-remote";

    [Fact]
    public void WhenTheLineReportsReachable_ReturnsReachable()
    {
        HostProbeOutputParser.Parse($"some noise\n{ReachableLine}\n").ShouldBe(new HostProbeOutcome.Reachable());
    }

    [Fact]
    public void WhenTheLineReportsUnreachable_ReturnsUnreachableWithGitsError()
    {
        HostProbeOutputParser.Parse(DnsFailureLine + "\n").ShouldBe(new HostProbeOutcome.Unreachable(
            "fatal: unable to access 'https://foundry-probe.invalid/x.git/': Could not resolve host: foundry-probe.invalid"));
    }

    [Fact]
    public void WhenTheLineCarriesADockerTimestamp_ReadsPastIt()
    {
        HostProbeOutputParser.Parse($"2026-11-13T07:40:00.123456789Z {ReachableLine}\r\n").ShouldBe(new HostProbeOutcome.Reachable());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CLONE_URL must be an https:// URL\n")]
    [InlineData("FOUNDRY_PROBE result=maybe detail=x\n")]
    [InlineData("echo FOUNDRY_PROBE result=reachable detail=x\n")]
    [InlineData("FOUNDRY_PROBE result=reachable detail=a\nFOUNDRY_PROBE result=unreachable detail=b\n")]
    public void WhenThereIsNotExactlyOneProbeLine_ReturnsInconclusive(string? log)
    {
        HostProbeOutputParser.Parse(log).ShouldBeOfType<HostProbeOutcome.Inconclusive>();
    }

    [Fact]
    public void WhenTheDetailHoldsAToken_ItIsRedacted()
    {
        HostProbeOutcome outcome = HostProbeOutputParser.Parse(
            "FOUNDRY_PROBE result=unreachable detail=fatal: unable to access 'https://oauth2:ghp_abcdefghijklmnopqrstuvwxyz0123456789@github.com/o/r.git/'\n");

        outcome.ShouldBeOfType<HostProbeOutcome.Unreachable>().Detail.ShouldNotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789");
    }
}
