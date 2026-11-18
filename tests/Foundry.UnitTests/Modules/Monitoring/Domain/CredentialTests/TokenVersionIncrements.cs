using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class TokenVersionIncrements
{
    private static BaseUrl GitHubBaseUrl() => BaseUrl.Create("https://github.com").ValueOrThrow();

    private static string Token(string suffix) => string.Concat("ghp_", suffix);

    [Fact]
    public void WhenAccountIsCreatedWithToken_VersionIsOne()
    {
        // Act
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Assert
        credential.TokenVersion.ShouldBe(1);
    }

    [Fact]
    public void WhenAccountTokenIsSet_VersionIncrements()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Act
        credential.Update(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("b"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Assert
        credential.TokenVersion.ShouldBe(2);
    }

    [Fact]
    public void WhenAccountIsUpdatedWithoutToken_VersionIsUnchanged()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Act
        credential.Update(AccountName.Create("renamed").ValueOrThrow(), token: null, GitHubBaseUrl());

        // Assert
        credential.TokenVersion.ShouldBe(1);
    }

    [Fact]
    public void WhenBranchRulesTokenIsSet_VersionIsUnchanged()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Act
        credential.SetBranchRulesToken(Token("rules"));

        // Assert
        credential.TokenVersion.ShouldBe(1);
    }

    [Fact]
    public void WhenTokenVerdictIsRecorded_VersionIsUnchanged()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), new IssuedToken(Token("a"), new TokenExpiry.NotChecked()), GitHubBaseUrl());

        // Act
        credential.MarkTokenInvalid(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        credential.MarkTokenValid();

        // Assert
        credential.TokenVersion.ShouldBe(1);
    }
}
