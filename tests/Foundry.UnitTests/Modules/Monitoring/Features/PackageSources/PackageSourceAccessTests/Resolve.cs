using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.PackageSourceAccessTests;

public sealed class Resolve
{
    private const string RepositoryHost = "gitlab.com";
    private static readonly Uri GroupSourceUrl = new("https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json");
    private static readonly Uri ThirdPartySourceUrl = new("https://feed.example/v3/index.json");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static GitLabCredential Account() =>
        GitLabCredential.Create(
            AccountName.Create("deploy-bot").ValueOrThrow(),
            new IssuedToken("account-token", new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());

    private static PackageSource SourceWithCredential(Uri url)
    {
        PackageSource source = PackageSource.Create(url);
        source.SetCredential("ci-reader", "registered-token", Now);
        return source;
    }

    [Fact]
    public void WhenACredentialIsRegistered_SendsItInsteadOfTheAccount()
    {
        // Arrange
        PackageSource source = SourceWithCredential(GroupSourceUrl);

        // Act
        SourceReadCredential read = PackageSourceAccess.Resolve(source, GroupSourceUrl, RepositoryHost, Account(), "account-token");

        // Assert
        SourceReadCredential.Send send = read.ShouldBeOfType<SourceReadCredential.Send>();
        send.ShouldSatisfyAllConditions(
            () => send.Credential.ShouldBeOfType<ProbeCredential.Registered>(),
            () => send.Version.ShouldBe(source.CredentialVersion),
            () => send.Upstream.ShouldNotBeNull().Username.ShouldBe("ci-reader"),
            () => send.Upstream.ShouldNotBeNull().Token.ShouldBe("registered-token"));
    }

    [Fact]
    public void WhenACredentialIsRegisteredForASourceOnAnotherHost_SendsIt()
    {
        // Arrange
        PackageSource source = SourceWithCredential(ThirdPartySourceUrl);

        // Act
        SourceReadCredential read = PackageSourceAccess.Resolve(source, ThirdPartySourceUrl, RepositoryHost, Account(), "account-token");

        // Assert
        read.ShouldBeOfType<SourceReadCredential.Send>().Upstream.ShouldNotBeNull().Username.ShouldBe("ci-reader");
    }

    [Fact]
    public void WhenNoneIsRegisteredAndTheSourceIsOnTheRepositoryHost_SendsTheAccount()
    {
        // Arrange
        GitLabCredential account = Account();

        // Act
        SourceReadCredential read = PackageSourceAccess.Resolve(
            PackageSource.Create(GroupSourceUrl),
            GroupSourceUrl,
            RepositoryHost,
            account,
            "account-token");

        // Assert
        SourceReadCredential.Send send = read.ShouldBeOfType<SourceReadCredential.Send>();
        send.ShouldSatisfyAllConditions(
            () => send.Credential.ShouldBe(new ProbeCredential.Account(account.Id)),
            () => send.Upstream.ShouldNotBeNull().Username.ShouldBe("deploy-bot"),
            () => send.Upstream.ShouldNotBeNull().Token.ShouldBe("account-token"));
    }

    [Fact]
    public void WhenNoneIsRegisteredAndTheSourceIsOnAnotherHost_SendsNothing()
    {
        // Act
        SourceReadCredential read = PackageSourceAccess.Resolve(
            source: null,
            ThirdPartySourceUrl,
            RepositoryHost,
            Account(),
            "account-token");

        // Assert
        SourceReadCredential.Send send = read.ShouldBeOfType<SourceReadCredential.Send>();
        send.ShouldSatisfyAllConditions(
            () => send.Credential.ShouldBeOfType<ProbeCredential.Anonymous>(),
            () => send.Upstream.ShouldBeNull());
    }

    [Fact]
    public void WhenTheRegisteredCredentialWasRemoved_FallsBackToTheAccount()
    {
        // Arrange
        PackageSource source = SourceWithCredential(GroupSourceUrl);
        source.RemoveCredential();

        // Act
        SourceReadCredential read = PackageSourceAccess.Resolve(source, GroupSourceUrl, RepositoryHost, Account(), "account-token");

        // Assert
        read.Credential.ShouldBeOfType<ProbeCredential.Account>();
    }
}
