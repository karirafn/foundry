using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.PackageSourceTests;

public sealed class RegisteredCredential
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static PackageSource NewSource() =>
        PackageSource.Create(new Uri("https://gitlab.com/api/v4/groups/1/-/packages/nuget/index.json"));

    [Fact]
    public void WhenNoneIsRegistered_IsNull()
    {
        // Act
        PackageSourceCredential? credential = NewSource().Credential;

        // Assert
        credential.ShouldBeNull();
    }

    [Fact]
    public void SetCredential_StoresTheUsernameTokenAndTime()
    {
        // Arrange
        PackageSource source = NewSource();

        // Act
        source.SetCredential("ci-reader", "secret-token", Now);

        // Assert
        source.Credential.ShouldNotBeNull().ShouldSatisfyAllConditions(
            c => c.Username.ShouldBe("ci-reader"),
            c => c.Token.ShouldBe(new ProviderToken.Present("secret-token")),
            c => c.UpdatedAt.ShouldBe(Now));
    }

    [Fact]
    public void SetCredential_WhenOneIsRegistered_ReplacesIt()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("old-reader", "old-token", Now);

        // Act
        source.SetCredential("new-reader", "new-token", Now.AddMinutes(5));

        // Assert
        source.Credential.ShouldNotBeNull().ShouldSatisfyAllConditions(
            c => c.Username.ShouldBe("new-reader"),
            c => c.Token.ShouldBe(new ProviderToken.Present("new-token")),
            c => c.UpdatedAt.ShouldBe(Now.AddMinutes(5)));
    }

    [Theory]
    [InlineData("", "token")]
    [InlineData("user", "")]
    public void SetCredential_WhenAFieldIsEmpty_Throws(string username, string token)
    {
        // Arrange
        PackageSource source = NewSource();

        // Act & Assert
        Should.Throw<ArgumentException>(() => source.SetCredential(username, token, Now));
        source.Credential.ShouldBeNull();
    }

    [Fact]
    public void RemoveCredential_ClearsIt()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "secret-token", Now);

        // Act
        source.RemoveCredential();

        // Assert
        source.Credential.ShouldBeNull();
    }

    [Fact]
    public void RemoveCredential_DropsTheRegisteredCredentialsVerdict()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "secret-token", Now);
        source.RecordProbe(new ProbeCredential.Registered(), source.CredentialVersion, new PackageSourceVerdict.Readable(), Now);

        // Act
        source.RemoveCredential();

        // Assert
        source.Probes.ShouldBeEmpty();
    }

    [Fact]
    public void CredentialFor_WhenOneIsRegistered_IsTheRegisteredCredential()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "secret-token", Now);

        // Act
        ProbeCredential credential = source.CredentialFor(new ProbeCredential.Account(CredentialId.New()));

        // Assert
        credential.ShouldBeOfType<ProbeCredential.Registered>();
    }

    [Fact]
    public void CredentialFor_WhenNoneIsRegistered_IsTheFallback()
    {
        // Arrange
        ProbeCredential account = new ProbeCredential.Account(CredentialId.New());

        // Act
        ProbeCredential credential = NewSource().CredentialFor(account);

        // Assert
        credential.ShouldBe(account);
    }

    [Fact]
    public void VerdictFor_TheRegisteredCredential_AfterItIsReplaced_IsUnchecked()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "old-token", Now);
        source.RecordProbe(new ProbeCredential.Registered(), source.CredentialVersion, new PackageSourceVerdict.Readable(), Now);

        // Act
        source.SetCredential("ci-reader", "new-token", Now.AddMinutes(1));

        // Assert
        source.VerdictFor(new ProbeCredential.Registered()).ShouldBeOfType<PackageSourceVerdict.Unchecked>();
    }

    [Fact]
    public void RecordProbe_WithACredentialSinceReplaced_IsDropped()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "old-token", Now);
        int oldVersion = source.CredentialVersion;
        source.SetCredential("ci-reader", "new-token", Now);
        source.RecordProbe(new ProbeCredential.Registered(), source.CredentialVersion, new PackageSourceVerdict.Readable(), Now);

        // Act
        source.RecordProbe(
            new ProbeCredential.Registered(),
            oldVersion,
            new PackageSourceVerdict.Unreadable(401, "service index returned 401"),
            Now.AddMinutes(1));

        // Assert
        source.VerdictFor(new ProbeCredential.Registered()).ShouldBeOfType<PackageSourceVerdict.Readable>();
    }

    [Fact]
    public void ToString_DoesNotShowTheToken()
    {
        // Arrange
        PackageSource source = NewSource();
        source.SetCredential("ci-reader", "secret-token", Now);

        // Act
        string text = source.Credential!.ToString();

        // Assert
        text.ShouldNotContain("secret-token");
    }
}
