using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.PackageSourceTests;

public sealed class IsDueForProbe
{
    private static readonly DateTimeOffset ProbedAt = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly ProbeCredential Account = new ProbeCredential.Account(CredentialId.New());

    private static PackageSource NewSource() =>
        PackageSource.Create(new Uri("https://gitlab.com/api/v4/groups/1/-/packages/nuget/index.json"));

    [Fact]
    public void WhenNeverProbedWithTheCredential_IsDue()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(new ProbeCredential.Anonymous(), 0, new PackageSourceVerdict.Readable(), ProbedAt);

        // Act
        bool due = source.IsDueForProbe(Account, tokenVersion: 1, ProbedAt);

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void WhenReadableWithinItsInterval_IsNotDue()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(Account, 1, new PackageSourceVerdict.Readable(), ProbedAt);

        // Act
        bool due = source.IsDueForProbe(Account, 1, ProbedAt + TimeSpan.FromMinutes(29));

        // Assert
        due.ShouldBeFalse();
    }

    [Fact]
    public void WhenReadableAndThirtyMinutesPassed_IsDue()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(Account, 1, new PackageSourceVerdict.Readable(), ProbedAt);

        // Act
        bool due = source.IsDueForProbe(Account, 1, ProbedAt + PackageSource.ReadableRecheckInterval);

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void WhenUnreadableWithinTheCooldown_IsNotDue()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(Account, 1, new PackageSourceVerdict.Unreadable(401, "service index returned 401"), ProbedAt);

        // Act
        bool due = source.IsDueForProbe(Account, 1, ProbedAt + TimeSpan.FromMinutes(14));

        // Assert
        due.ShouldBeFalse();
    }

    [Fact]
    public void WhenUnreadableAndFifteenMinutesPassed_IsDue()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(Account, 1, new PackageSourceVerdict.Unreadable(null, "service index request failed"), ProbedAt);

        // Act
        bool due = source.IsDueForProbe(Account, 1, ProbedAt + TimeSpan.FromMinutes(15));

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void UnreadableCooldown_MatchesTheAccountTokenRecheckCooldown()
    {
        PackageSource.UnreadableRecheckCooldown.ShouldBe(Credential.TokenRecheckCooldown);
    }

    [Fact]
    public void WhenTheAccountTokenWasReplacedSinceTheProbe_IsDueAtOnce()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(Account, 1, new PackageSourceVerdict.Readable(), ProbedAt);

        // Act
        bool due = source.IsDueForProbe(Account, tokenVersion: 2, ProbedAt + TimeSpan.FromSeconds(30));

        // Assert
        due.ShouldBeTrue();
    }
}
