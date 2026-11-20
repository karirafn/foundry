using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.PackageProxyPauseTests;

public sealed class AfterUnreachableProbe
{
    private static readonly DateTimeOffset PausedAt = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public void WhenStarted_FirstProbeIsAMinuteLaterWithNoAttempts()
    {
        // Act
        PackageProxyPause pause = PackageProxyPause.Start(PausedAt);

        // Assert
        pause.ShouldSatisfyAllConditions(
            () => pause.PausedAt.ShouldBe(PausedAt),
            () => pause.ProbeAttempts.ShouldBe(0),
            () => pause.NextProbeAt.ShouldBe(PausedAt.AddMinutes(1)));
    }

    [Fact]
    public void WhenAProbeFails_CountsTheAttemptAndKeepsPausedAt()
    {
        // Arrange
        PackageProxyPause pause = PackageProxyPause.Start(PausedAt);
        DateTimeOffset probedAt = PausedAt.AddMinutes(1);

        // Act
        PackageProxyPause advanced = pause.AfterUnreachableProbe(probedAt);

        // Assert
        advanced.ShouldSatisfyAllConditions(
            () => advanced.PausedAt.ShouldBe(PausedAt),
            () => advanced.ProbeAttempts.ShouldBe(1),
            () => advanced.NextProbeAt.ShouldBe(probedAt.AddMinutes(2)));
    }

    [Fact]
    public void WhenProbesKeepFailing_TheyAre1_2_4_8MinutesApartThenEvery10()
    {
        // Arrange
        PackageProxyPause pause = PackageProxyPause.Start(PausedAt);
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
        PackageProxyPause pause = PackageProxyPause.Start(PausedAt);
        for (int probe = 0; probe < 200; probe++)
        {
            pause = pause.AfterUnreachableProbe(PausedAt);
        }

        DateTimeOffset probedAt = PausedAt.AddHours(40);

        // Act
        PackageProxyPause advanced = pause.AfterUnreachableProbe(probedAt);

        // Assert
        advanced.ShouldSatisfyAllConditions(
            () => advanced.ProbeAttempts.ShouldBe(201),
            () => advanced.NextProbeAt.ShouldBe(probedAt.AddMinutes(10)));
    }
}
