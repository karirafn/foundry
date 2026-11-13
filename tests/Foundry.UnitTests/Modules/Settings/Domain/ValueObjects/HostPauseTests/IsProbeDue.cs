using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.HostPauseTests;

public sealed class IsProbeDue
{
    private static readonly DateTimeOffset PausedAt = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public void BeforeNextProbeAt_IsFalse()
    {
        HostPause pause = HostPause.Start("gitlab.com", PausedAt);

        pause.IsProbeDue(PausedAt.AddSeconds(59)).ShouldBeFalse();
    }

    [Fact]
    public void AtOrAfterNextProbeAt_IsTrue()
    {
        HostPause pause = HostPause.Start("gitlab.com", PausedAt);

        pause.ShouldSatisfyAllConditions(
            () => pause.IsProbeDue(PausedAt.AddMinutes(1)).ShouldBeTrue(),
            () => pause.IsProbeDue(PausedAt.AddMinutes(5)).ShouldBeTrue());
    }
}
