using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class RecordTokenExpiry
{
    private static readonly AccountName Name = AccountName.Create("octocat").ValueOrThrow();
    private static readonly BaseUrl GitHubUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
    private static readonly TokenExpiry ReadExpiry = new TokenExpiry.On(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void WhenExpiryIsNotChecked_RecordsTheReadExpiry()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(Name, new IssuedToken("ghp_token", new TokenExpiry.NotChecked()), GitHubUrl);

        // Act
        credential.RecordTokenExpiry(ReadExpiry);

        // Assert
        credential.TokenExpiry.ShouldBe(ReadExpiry);
    }

    [Theory]
    [MemberData(nameof(KnownExpiries))]
    public void WhenExpiryIsAlreadyKnown_KeepsTheStoredExpiry(TokenExpiry stored)
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(Name, new IssuedToken("ghp_token", stored), GitHubUrl);

        // Act
        credential.RecordTokenExpiry(ReadExpiry);

        // Assert
        credential.TokenExpiry.ShouldBe(stored);
    }

    [Fact]
    public void WhenExpiryIsRecorded_TokenVersionIsUnchanged()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(Name, new IssuedToken("ghp_token", new TokenExpiry.NotChecked()), GitHubUrl);
        int version = credential.TokenVersion;

        // Act
        credential.RecordTokenExpiry(ReadExpiry);

        // Assert
        credential.TokenVersion.ShouldBe(version);
    }

    public static TheoryData<TokenExpiry> KnownExpiries() =>
    [
        new TokenExpiry.Unavailable(),
        new TokenExpiry.Never(),
        new TokenExpiry.On(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)),
    ];
}
