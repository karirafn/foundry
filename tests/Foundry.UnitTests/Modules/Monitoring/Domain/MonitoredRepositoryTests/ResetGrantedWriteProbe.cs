using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class ResetGrantedWriteProbe
{
    private const string Version = "github-current";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static MonitoredRepository CreateRepository(WriteProbeVerdict verdict, RepositoryEligibility eligibility)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("octocat/hello-world").ValueOrThrow(), "github.com", null).ValueOrThrow();
        repository.SetWriteProbeVerdict(verdict);
        repository.SetEligibility(eligibility);
        return repository;
    }

    [Fact]
    public void WhenVerdictIsGranted_ResetsVerdictToUnknownAccessRefusedKeepingVersion()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(
            new WriteProbeVerdict.Granted { PermissionListVersion = Version },
            new RepositoryEligibility.Eligible());

        // Act
        bool reset = repository.ResetGrantedWriteProbe();

        // Assert
        reset.ShouldBeTrue();
        repository.WriteProbeVerdict.ShouldBe(
            new WriteProbeVerdict.Unknown(LastAttemptedAt: null, Reason: UnknownReason.AccessRefused)
            {
                PermissionListVersion = Version,
            });
    }

    [Fact]
    public void WhenVerdictIsGranted_SetsEligibilityToUnreachableAccessRefused()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(
            new WriteProbeVerdict.Granted { PermissionListVersion = Version },
            new RepositoryEligibility.Eligible());

        // Act
        repository.ResetGrantedWriteProbe();

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.Eligibility.ShouldBe(new RepositoryEligibility.Unreachable(UnreachableReason.AccessRefused)),
            () => repository.EligibilityStatus.ShouldBe("unreachable"));
    }

    private static WriteProbeVerdict NonGrantedVerdict(string name) =>
        name switch
        {
            "denied" => new WriteProbeVerdict.Denied(Now.AddMinutes(-5)) { PermissionListVersion = Version },
            "rate-limited" => new WriteProbeVerdict.Unknown(Now.AddMinutes(-5), UnknownReason.RateLimited) { PermissionListVersion = Version },
            "transport" => new WriteProbeVerdict.Unknown(Now.AddMinutes(-5), UnknownReason.Transport) { PermissionListVersion = Version },
            "access-refused" => new WriteProbeVerdict.Unknown(null, UnknownReason.AccessRefused) { PermissionListVersion = Version },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

    [Theory]
    [InlineData("denied")]
    [InlineData("rate-limited")]
    [InlineData("transport")]
    [InlineData("access-refused")]
    public void WhenVerdictIsNotGranted_ChangesNothing(string verdictName)
    {
        // Arrange
        WriteProbeVerdict verdict = NonGrantedVerdict(verdictName);
        RepositoryEligibility eligibility = new RepositoryEligibility.Unreachable(UnreachableReason.RateLimited);
        MonitoredRepository repository = CreateRepository(verdict, eligibility);

        // Act
        bool reset = repository.ResetGrantedWriteProbe();

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => reset.ShouldBeFalse(),
            () => repository.WriteProbeVerdict.ShouldBe(verdict),
            () => repository.Eligibility.ShouldBe(eligibility));
    }

    [Fact]
    public void WhenCalledTwice_OnlyTheFirstResets()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(
            new WriteProbeVerdict.Granted { PermissionListVersion = Version },
            new RepositoryEligibility.Eligible());
        repository.ResetGrantedWriteProbe();

        // Act
        bool second = repository.ResetGrantedWriteProbe();

        // Assert
        second.ShouldBeFalse();
    }
}
