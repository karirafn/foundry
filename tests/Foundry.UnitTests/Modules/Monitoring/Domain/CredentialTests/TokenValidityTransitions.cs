using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class TokenValidityTransitions
{
    private static readonly DateTimeOffset Detected = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static BaseUrl GitHubBaseUrl() => BaseUrl.Create("https://github.com").ValueOrThrow();

    private static BaseUrl GitLabBaseUrl() => BaseUrl.Create("https://gitlab.com").ValueOrThrow();

    private static string Token(string suffix) => string.Concat("ghp_", suffix);

    [Fact]
    public void WhenAccountIsCreated_TokenIsValid()
    {
        // Act
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), Token("a"), GitHubBaseUrl());

        // Assert
        credential.TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
    }

    [Fact]
    public void WhenValidTokenIsMarkedInvalid_DetectedAtAndLastCheckedAtAreNow()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), Token("a"), GitHubBaseUrl());

        // Act
        credential.MarkTokenInvalid(Detected);

        // Assert
        credential.TokenValidity.ShouldBe(new TokenValidity.Invalid(Detected, Detected));
    }

    [Fact]
    public void WhenInvalidTokenIsMarkedInvalidAgain_KeepsDetectedAtAndStampsLastCheckedAt()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), Token("a"), GitHubBaseUrl());
        credential.MarkTokenInvalid(Detected);
        DateTimeOffset later = Detected.AddMinutes(20);

        // Act
        credential.MarkTokenInvalid(later);

        // Assert
        credential.TokenValidity.ShouldBe(new TokenValidity.Invalid(Detected, later));
    }

    [Fact]
    public void WhenInvalidTokenIsMarkedValid_TokenIsValid()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), Token("a"), GitHubBaseUrl());
        credential.MarkTokenInvalid(Detected);

        // Act
        credential.MarkTokenValid();

        // Assert
        credential.TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
    }

    [Fact]
    public void WhenGitHubAccountGetsNewToken_InvalidTokenBecomesValid()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), Token("a"), GitHubBaseUrl());
        credential.MarkTokenInvalid(Detected);

        // Act
        credential.Update(AccountName.Create("my-org").ValueOrThrow(), Token("b"), GitHubBaseUrl());

        // Assert
        credential.TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
    }

    [Fact]
    public void WhenGitLabAccountGetsNewToken_InvalidTokenBecomesValid()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create(AccountName.Create("my-group").ValueOrThrow(), Token("a"), GitLabBaseUrl());
        credential.MarkTokenInvalid(Detected);

        // Act
        credential.Update(AccountName.Create("my-group").ValueOrThrow(), Token("b"), GitLabBaseUrl());

        // Assert
        credential.TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
    }

    [Fact]
    public void WhenAccountIsUpdatedWithoutToken_InvalidTokenStaysInvalid()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), Token("a"), GitHubBaseUrl());
        credential.MarkTokenInvalid(Detected);

        // Act
        credential.Update(AccountName.Create("renamed").ValueOrThrow(), token: null, GitHubBaseUrl());

        // Assert
        credential.TokenValidity.ShouldBe(new TokenValidity.Invalid(Detected, Detected));
    }
}
