using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.PackageSourceTests;

public sealed class ReadabilityChangedEvent
{
    private static readonly DateTimeOffset ProbedAt = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly ProbeCredential AccountCredential = new ProbeCredential.Account(CredentialId.New());

    private static PackageSource NewSource() =>
        PackageSource.Create(new Uri("https://gitlab.com/api/v4/groups/1/-/packages/nuget/index.json"));

    private static PackageSource SourceProbedWith(PackageSourceVerdict.Probed verdict)
    {
        PackageSource source = NewSource();
        source.RecordProbe(AccountCredential, 1, verdict, ProbedAt);
        source.ClearDomainEvents();
        return source;
    }

    [Fact]
    public void WhenUnreadableBecomesReadable_IsRaised()
    {
        // Arrange
        PackageSource source = SourceProbedWith(new PackageSourceVerdict.Unreadable(401, "service index returned 401"));

        // Act
        source.RecordProbe(AccountCredential, 1, new PackageSourceVerdict.Readable(), ProbedAt.AddMinutes(15));

        // Assert
        source.DomainEvents.ShouldBe([new PackageSourceReadabilityChanged(source.Id, source.ServiceIndexUrl)]);
    }

    [Fact]
    public void WhenReadableBecomesUnreadable_IsRaised()
    {
        // Arrange
        PackageSource source = SourceProbedWith(new PackageSourceVerdict.Readable());

        // Act
        source.RecordProbe(AccountCredential, 1, new PackageSourceVerdict.Unreadable(403, "search returned 403"), ProbedAt.AddMinutes(30));

        // Assert
        source.DomainEvents.ShouldBe([new PackageSourceReadabilityChanged(source.Id, source.ServiceIndexUrl)]);
    }

    [Fact]
    public void WhenReadableStaysReadable_IsNotRaised()
    {
        // Arrange
        PackageSource source = SourceProbedWith(new PackageSourceVerdict.Readable());

        // Act
        source.RecordProbe(AccountCredential, 1, new PackageSourceVerdict.Readable(), ProbedAt.AddMinutes(30));

        // Assert
        source.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenUnreadableStaysUnreadableForAnotherReason_IsNotRaised()
    {
        // Arrange
        PackageSource source = SourceProbedWith(new PackageSourceVerdict.Unreadable(401, "service index returned 401"));

        // Act
        source.RecordProbe(AccountCredential, 1, new PackageSourceVerdict.Unreadable(null, "connection refused"), ProbedAt.AddMinutes(15));

        // Assert
        source.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenTheFirstProbeIsReadable_IsRaised()
    {
        // Arrange
        PackageSource source = NewSource();

        // Act
        source.RecordProbe(AccountCredential, 1, new PackageSourceVerdict.Readable(), ProbedAt);

        // Assert
        source.DomainEvents.ShouldBe([new PackageSourceReadabilityChanged(source.Id, source.ServiceIndexUrl)]);
    }

    [Fact]
    public void WhenTheFirstProbeIsUnreadable_IsNotRaised()
    {
        // Arrange
        PackageSource source = NewSource();

        // Act
        source.RecordProbe(AccountCredential, 1, new PackageSourceVerdict.Unreadable(401, "service index returned 401"), ProbedAt);

        // Assert
        source.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenAReplacedCredentialsReadableProbeIsFollowedByAnUnreadableOne_IsNotRaised()
    {
        // Arrange: the Readable probe belongs to the replaced credential, so the current verdict is Unchecked.
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "first-token", ProbedAt);
        source.RecordProbe(new ProbeCredential.Registered(), source.CredentialVersion, new PackageSourceVerdict.Readable(), ProbedAt);
        source.SetCredential("ci-reader", "second-token", ProbedAt.AddMinutes(1));
        source.ClearDomainEvents();

        // Act
        source.RecordProbe(
            new ProbeCredential.Registered(),
            source.CredentialVersion,
            new PackageSourceVerdict.Unreadable(401, "service index returned 401"),
            ProbedAt.AddMinutes(1));

        // Assert
        source.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenTheProbeIsDroppedForAReplacedCredential_IsNotRaised()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "first-token", ProbedAt);
        int staleVersion = source.CredentialVersion;
        source.SetCredential("ci-reader", "second-token", ProbedAt.AddMinutes(1));

        // Act
        source.RecordProbe(new ProbeCredential.Registered(), staleVersion, new PackageSourceVerdict.Readable(), ProbedAt.AddMinutes(1));

        // Assert
        source.DomainEvents.ShouldBeEmpty();
    }
}
