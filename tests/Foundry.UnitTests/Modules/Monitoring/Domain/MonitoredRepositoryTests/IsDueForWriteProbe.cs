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
    private const string CurrentVersion = "github-current";
    private const string OutdatedVersion = "github-outdated";
    private static readonly IReadOnlySet<string> CurrentVersions = new HashSet<string> { CurrentVersion };

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(ValidSlug, "github.com", null).ValueOrThrow();

    [Fact]
    public void WhenVerdictIsUnknownWithNullTimestamp_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(LastAttemptedAt: null) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsUnknownJustPastCooldown_ReturnsTrue()
    {
        // Arrange — LastAttemptedAt + cooldown < now → due
        DateTimeOffset justPast = Now.AddMinutes(-15).AddTicks(-1);
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(justPast) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsUnknownExactlyAtCooldownBoundary_ReturnsFalse()
    {
        // Arrange — LastAttemptedAt + cooldown == now → NOT due (strict < boundary, mirror IsDueForPoll)
        DateTimeOffset exactBoundary = Now.AddMinutes(-15);
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(exactBoundary) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsUnknownWithinCooldown_ReturnsFalse()
    {
        // Arrange — attempted 5 minutes ago, cooldown is 15 minutes
        DateTimeOffset recentAttempt = Now.AddMinutes(-5);
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(recentAttempt) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsGranted_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Granted() { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDeniedWithNullProbedAt_ReturnsTrue()
    {
        // Arrange — a Denied row persisted before ProbedAt existed
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(ProbedAt: null) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsDeniedWithinOneHour_ReturnsFalse()
    {
        // Arrange — denied at 11:30, polled at 12:00
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now.AddMinutes(-30)) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDeniedPastUnknownCooldownButWithinOneHour_ReturnsFalse()
    {
        // Arrange — the 15-minute Unknown cooldown does not apply to Denied
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now.AddMinutes(-16)) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDeniedExactlyAtOneHourBoundary_ReturnsFalse()
    {
        // Arrange — ProbedAt + cooldown == now → NOT due (strict < boundary, as for Unknown)
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now.AddHours(-1)) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenVerdictIsDeniedJustPastOneHour_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now.AddHours(-1).AddTicks(-1)) { PermissionListVersion = CurrentVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsGrantedWithOutdatedVersion_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Granted { PermissionListVersion = OutdatedVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsGrantedWithoutVersion_ReturnsTrue()
    {
        // Arrange — a verdict persisted before permission-list versions existed
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Granted());

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsDeniedWithinCooldownButOutdated_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(
            new WriteProbeVerdict.Denied(Now.AddMinutes(-1)) { PermissionListVersion = OutdatedVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenVerdictIsUnknownWithinCooldownButOutdated_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(
            new WriteProbeVerdict.Unknown(Now.AddMinutes(-1)) { PermissionListVersion = OutdatedVersion });

        // Act
        bool result = repository.IsDueForWriteProbe(Now, CurrentVersions);

        // Assert
        result.ShouldBeTrue();
    }
}
