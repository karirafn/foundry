using Foundry.Modules.Settings.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.HostPauseTests;

public sealed class Start
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public void WhenStarted_HasNoProbeAttemptsAndProbesAMinuteLater()
    {
        // Act
        HostPause pause = HostPause.Start("gitlab.com", Now);

        // Assert
        pause.ShouldSatisfyAllConditions(
            () => pause.Host.ShouldBe("gitlab.com"),
            () => pause.PausedAt.ShouldBe(Now),
            () => pause.ProbeAttempts.ShouldBe(0),
            () => pause.NextProbeAt.ShouldBe(Now.AddMinutes(1)));
    }

    [Fact]
    public void WhenHostHasCapitalsAndWhitespace_StoresItTrimmedAndLowerCase()
    {
        // Act
        HostPause pause = HostPause.Start("  GitLab.COM ", Now);

        // Assert
        pause.Host.ShouldBe("gitlab.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenHostIsBlank_Throws(string host)
    {
        // Act / Assert
        Should.Throw<ArgumentException>(() => HostPause.Start(host, Now));
    }
}
