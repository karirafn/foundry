using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.HostPauseTests;

public sealed class AfterUnreachableProbe
{
    private static readonly DateTimeOffset PausedAt = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public void WhenAProbeFails_CountsTheAttemptAndKeepsHostAndPausedAt()
    {
        // Arrange
        HostPause pause = HostPause.Start("gitlab.com", PausedAt);
        DateTimeOffset probedAt = PausedAt.AddMinutes(1);

        // Act
        HostPause advanced = pause.AfterUnreachableProbe(probedAt);

        // Assert
        advanced.ShouldSatisfyAllConditions(
            () => advanced.Host.ShouldBe("gitlab.com"),
            () => advanced.PausedAt.ShouldBe(PausedAt),
            () => advanced.ProbeAttempts.ShouldBe(1),
            () => advanced.NextProbeAt.ShouldBe(probedAt.AddMinutes(2)));
    }

    [Fact]
    public void WhenProbesKeepFailing_TheyAre1_2_4_8MinutesApartThenEvery10()
    {
        // Arrange
        HostPause pause = HostPause.Start("gitlab.com", PausedAt);
        List<double> offsets = [(pause.NextProbeAt - PausedAt).TotalMinutes];

        // Act: each probe runs exactly when it is due.
        for (int probe = 0; probe < 6; probe++)
        {
            pause = pause.AfterUnreachableProbe(pause.NextProbeAt);
            offsets.Add((pause.NextProbeAt - PausedAt).TotalMinutes);
        }

        // Assert
        offsets.ShouldBe([1, 3, 7, 15, 25, 35, 45]);
    }

    [Fact]
    public void WhenManyProbesHaveFailed_TheDelayStaysAt10Minutes()
    {
        // Arrange
        HostPause pause = HostPause.Start("gitlab.com", PausedAt);
        for (int probe = 0; probe < 200; probe++)
        {
            pause = pause.AfterUnreachableProbe(PausedAt);
        }

        DateTimeOffset probedAt = PausedAt.AddHours(40);

        // Act
        HostPause advanced = pause.AfterUnreachableProbe(probedAt);

        // Assert
        advanced.ShouldSatisfyAllConditions(
            () => advanced.ProbeAttempts.ShouldBe(201),
            () => advanced.NextProbeAt.ShouldBe(probedAt.AddMinutes(10)));
    }

    [Fact]
    public void WhenAProbeFails_TheOriginalIsUnchanged()
    {
        // Arrange
        HostPause pause = HostPause.Start("gitlab.com", PausedAt);

        // Act
        _ = pause.AfterUnreachableProbe(PausedAt.AddMinutes(1));

        // Assert
        pause.ShouldSatisfyAllConditions(
            () => pause.ProbeAttempts.ShouldBe(0),
            () => pause.NextProbeAt.ShouldBe(PausedAt.AddMinutes(1)));
    }
}
