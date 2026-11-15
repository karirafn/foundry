using Foundry.Modules.Workers.Features.ImageBuild;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.FoundryImageSweepTests;

public sealed class FoundryImageSweepServiceTests
{
    [Fact]
    public void SweepsEveryHour()
    {
        FoundryImageSweepService.Interval.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task EachTickRemovesUntaggedFoundryImages()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:left-by-a-finished-run");
        using FoundryImageSweepService sut = new(
            new FoundryImageSweep(host, NullLogger<FoundryImageSweep>.Instance),
            NullLogger<FoundryImageSweepService>.Instance);

        await sut.TickForTest(TestContext.Current.CancellationToken);

        host.RemainingIds.ShouldBeEmpty();
    }
}
