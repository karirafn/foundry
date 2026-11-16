using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.PackageSourceTests;

public sealed class VerdictFor
{
    private static readonly DateTimeOffset ProbedAt = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static PackageSource NewSource() =>
        PackageSource.Create(new Uri("https://gitlab.com/api/v4/groups/1/-/packages/nuget/index.json"));

    [Fact]
    public void WhenNeverProbed_IsUnchecked()
    {
        // Act
        PackageSourceVerdict verdict = NewSource().VerdictFor(new ProbeCredential.Anonymous());

        // Assert
        verdict.ShouldBeOfType<PackageSourceVerdict.Unchecked>();
    }

    [Fact]
    public void WhenProbedWithAnotherCredential_IsUnchecked()
    {
        // Arrange
        PackageSource source = NewSource();
        source.RecordProbe(new ProbeCredential.Account(CredentialId.New()), 1, new PackageSourceVerdict.Readable(), ProbedAt);

        // Act
        PackageSourceVerdict verdict = source.VerdictFor(new ProbeCredential.Account(CredentialId.New()));

        // Assert
        verdict.ShouldBeOfType<PackageSourceVerdict.Unchecked>();
    }

    [Fact]
    public void WhenProbedAgain_KeepsOnlyTheLatestVerdictPerCredential()
    {
        // Arrange
        PackageSource source = NewSource();
        ProbeCredential account = new ProbeCredential.Account(CredentialId.New());
        source.RecordProbe(account, 1, new PackageSourceVerdict.Unreadable(401, "service index returned 401"), ProbedAt);

        // Act
        source.RecordProbe(account, 1, new PackageSourceVerdict.Readable(), ProbedAt.AddMinutes(15));

        // Assert
        source.ShouldSatisfyAllConditions(
            () => source.VerdictFor(account).ShouldBeOfType<PackageSourceVerdict.Readable>(),
            () => source.Probes.ShouldHaveSingleItem().CheckedAt.ShouldBe(ProbedAt.AddMinutes(15)));
    }

    [Fact]
    public void WhenTwoAccountsProbeOneSource_KeepsEachAccountsVerdict()
    {
        // Arrange
        PackageSource source = NewSource();
        ProbeCredential reader = new ProbeCredential.Account(CredentialId.New());
        ProbeCredential outsider = new ProbeCredential.Account(CredentialId.New());

        // Act
        source.RecordProbe(reader, 1, new PackageSourceVerdict.Readable(), ProbedAt);
        source.RecordProbe(outsider, 1, new PackageSourceVerdict.Unreadable(403, "search returned 403"), ProbedAt);

        // Assert
        source.ShouldSatisfyAllConditions(
            () => source.VerdictFor(reader).ShouldBeOfType<PackageSourceVerdict.Readable>(),
            () => source.VerdictFor(outsider).ShouldBe(new PackageSourceVerdict.Unreadable(403, "search returned 403")));
    }
}
