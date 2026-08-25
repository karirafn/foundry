using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class IsDueForWriteProbe
{
    private static readonly TimeSpan Cooldown = MonitoredRepository.WriteProbeCooldown;

    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(ValidSlug, "github.com", null);

    [Fact]
    public void WhenVerdictIsUnknownWithNullTimestamp_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(LastAttemptedAt: null));

        // Act
        bool result = repository.IsDueForWriteProbe(Cooldown, Now);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsUnknownJustPastCooldown_ReturnsTrue()
    {
        // Arrange — LastAttemptedAt + cooldown < now → due
        DateTimeOffset justPast = Now.AddMinutes(-15).AddTicks(-1);
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(justPast));

        // Act
        bool result = repository.IsDueForWriteProbe(Cooldown, Now);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsUnknownExactlyAtCooldownBoundary_ReturnsFalse()
    {
        // Arrange — LastAttemptedAt + cooldown == now → NOT due (strict < boundary, mirror IsDueForPoll)
        DateTimeOffset exactBoundary = Now.AddMinutes(-15);
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(exactBoundary));

        // Act
        bool result = repository.IsDueForWriteProbe(Cooldown, Now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsUnknownWithinCooldown_ReturnsFalse()
    {
        // Arrange — attempted 5 minutes ago, cooldown is 15 minutes
        DateTimeOffset recentAttempt = Now.AddMinutes(-5);
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(recentAttempt));

        // Act
        bool result = repository.IsDueForWriteProbe(Cooldown, Now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsGranted_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Granted());

        // Act
        bool result = repository.IsDueForWriteProbe(Cooldown, Now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDenied_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied());

        // Act
        bool result = repository.IsDueForWriteProbe(Cooldown, Now);

        // Assert
        result.ShouldBeFalse();
    }
}
