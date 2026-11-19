using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class IsDueForWriteProbe
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(ValidSlug, "github.com", null).ValueOrThrow();

    [Fact]
    public void WhenVerdictIsUnknownWithNullTimestamp_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(LastAttemptedAt: null));

        // Act
        bool result = repository.IsDueForWriteProbe(Now);

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
        bool result = repository.IsDueForWriteProbe(Now);

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
        bool result = repository.IsDueForWriteProbe(Now);

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
        bool result = repository.IsDueForWriteProbe(Now);

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
        bool result = repository.IsDueForWriteProbe(Now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDeniedWithNullProbedAt_ReturnsTrue()
    {
        // Arrange — a Denied row persisted before ProbedAt existed
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(ProbedAt: null));

        // Act
        bool result = repository.IsDueForWriteProbe(Now);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsDeniedWithinOneHour_ReturnsFalse()
    {
        // Arrange — denied at 11:30, polled at 12:00
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now.AddMinutes(-30)));

        // Act
        bool result = repository.IsDueForWriteProbe(Now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDeniedPastUnknownCooldownButWithinOneHour_ReturnsFalse()
    {
        // Arrange — the 15-minute Unknown cooldown does not apply to Denied
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now.AddMinutes(-16)));

        // Act
        bool result = repository.IsDueForWriteProbe(Now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDeniedExactlyAtOneHourBoundary_ReturnsFalse()
    {
        // Arrange — ProbedAt + cooldown == now → NOT due (strict < boundary, as for Unknown)
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now.AddHours(-1)));

        // Act
        bool result = repository.IsDueForWriteProbe(Now);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDeniedJustPastOneHour_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now.AddHours(-1).AddTicks(-1)));

        // Act
        bool result = repository.IsDueForWriteProbe(Now);

        // Assert
        result.ShouldBeTrue();
    }
}
