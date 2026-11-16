using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.PackageSourceStatusRuleTests;

public sealed class For
{
    private static readonly DateTimeOffset Earlier = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddMinutes(10);

    private static readonly FallbackRead.ViaAccount Alpha = new(CredentialId.New(), "alpha-bot", "gitlab.com");
    private static readonly FallbackRead.ViaAccount Beta = new(CredentialId.New(), "beta-bot", "gitlab.com");

    private static PackageSource NewSource() =>
        PackageSource.Create(new Uri("https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json"));

    [Fact]
    public void WithARegisteredCredentialReadable_IsReachableViaCredential()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "token", Earlier);
        source.RecordProbe(new ProbeCredential.Registered(), source.CredentialVersion, new PackageSourceVerdict.Readable(), Later);
        source.RecordProbe(Alpha.Credential, 1, new PackageSourceVerdict.Unreadable(401, "service index returned 401"), Earlier);

        // Act
        ListedStatus status = PackageSourceStatusRule.For(source, [Alpha]);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.ReachableViaCredential(), Later));
    }

    [Fact]
    public void WithARegisteredCredentialUnreadable_IsUnreachable()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "token", Earlier);
        source.RecordProbe(
            new ProbeCredential.Registered(),
            source.CredentialVersion,
            new PackageSourceVerdict.Unreadable(403, "search returned 403"),
            Later);

        // Act
        ListedStatus status = PackageSourceStatusRule.For(source, [Alpha]);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.Unreachable(403, "search returned 403"), Later));
    }

    [Fact]
    public void WithAReplacedCredentialNotProbedYet_IsUncheckedKeepingTheEarlierProbesTime()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "old-token", Earlier);
        source.RecordProbe(new ProbeCredential.Registered(), source.CredentialVersion, new PackageSourceVerdict.Readable(), Earlier);
        source.SetCredential("ci-reader", "new-token", Later);

        // Act
        ListedStatus status = PackageSourceStatusRule.For(source, [Alpha]);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.Unchecked(), Earlier));
    }

    [Fact]
    public void WhenEveryAccountReadsIt_IsReachableViaTheFirstAccountByName()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(Beta.Credential, 1, new PackageSourceVerdict.Readable(), Earlier);
        source.RecordProbe(Alpha.Credential, 1, new PackageSourceVerdict.Readable(), Later);

        // Act
        ListedStatus status = PackageSourceStatusRule.For(source, [Beta, Alpha]);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.ReachableViaAccount("alpha-bot", "gitlab.com"), Later));
    }

    [Fact]
    public void WhenAnyAccountCannotReadIt_IsUnreachableWithThatAccountsVerdict()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(Alpha.Credential, 1, new PackageSourceVerdict.Readable(), Earlier);
        source.RecordProbe(Beta.Credential, 1, new PackageSourceVerdict.Unreadable(401, "service index returned 401"), Later);

        // Act
        ListedStatus status = PackageSourceStatusRule.For(source, [Alpha, Beta]);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.Unreachable(401, "service index returned 401"), Later));
    }

    [Fact]
    public void WhenReadWithoutACredential_IsReachableAnonymously()
    {
        // Arrange
        PackageSource source = NewSource();
        FallbackRead anonymous = new FallbackRead.Anonymous();
        source.RecordProbe(anonymous.Credential, 0, new PackageSourceVerdict.Readable(), Earlier);

        // Act
        ListedStatus status = PackageSourceStatusRule.For(source, [anonymous]);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.ReachableAnonymously(), Earlier));
    }

    [Fact]
    public void WhenTheAnonymousReadFails_IsUnreachable()
    {
        // Arrange
        PackageSource source = NewSource();
        FallbackRead anonymous = new FallbackRead.Anonymous();
        source.RecordProbe(Alpha.Credential, 1, new PackageSourceVerdict.Readable(), Earlier);
        source.RecordProbe(anonymous.Credential, 0, new PackageSourceVerdict.Unreadable(null, "probe timed out"), Later);

        // Act
        ListedStatus status = PackageSourceStatusRule.For(source, [anonymous, Alpha]);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.Unreachable(null, "probe timed out"), Later));
    }

    [Fact]
    public void WhenAnyReadIsNotProbedYet_IsUnchecked()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(Alpha.Credential, 1, new PackageSourceVerdict.Readable(), Earlier);

        // Act
        ListedStatus status = PackageSourceStatusRule.For(source, [Alpha, Beta]);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.Unchecked(), null));
    }

    [Fact]
    public void WithNoUsableRead_IsUnchecked()
    {
        // Act
        ListedStatus status = PackageSourceStatusRule.For(NewSource(), []);

        // Assert
        status.ShouldBe(new ListedStatus(new PackageSourceStatus.Unchecked(), null));
    }
}
