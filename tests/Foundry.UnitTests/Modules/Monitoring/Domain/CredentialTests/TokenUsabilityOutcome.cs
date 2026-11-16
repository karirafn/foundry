using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class TokenUsabilityOutcome
{
    private static BaseUrl GitHubBaseUrl() => BaseUrl.Create("https://github.com").ValueOrThrow();

    [Fact]
    public void WhenTokenIsPresent_ReturnsUsableCarryingTheToken()
    {
        // Arrange
        string token = string.Concat("ghp_", "usable");
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), token, GitHubBaseUrl());

        // Act
        TokenUsability result = credential.TokenUsability;

        // Assert
        result.ShouldBeOfType<TokenUsability.Usable>().Token.ShouldBe(token);
    }

    [Fact]
    public void WhenTokenIsUnreadable_ReturnsUnreadable()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.CreateWithUnreadableToken(AccountName.Create("my-org").ValueOrThrow(), GitHubBaseUrl());

        // Act
        TokenUsability result = credential.TokenUsability;

        // Assert
        result.ShouldBeOfType<TokenUsability.Unreadable>();
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsAbsent()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), null, GitHubBaseUrl());

        // Act
        TokenUsability result = credential.TokenUsability;

        // Assert
        result.ShouldBeOfType<TokenUsability.Absent>();
    }

    [Fact]
    public void WhenTokenIsPresentAndInvalid_ReturnsInvalidCarryingTheToken()
    {
        // Arrange
        string token = string.Concat("ghp_", "rejected");
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("my-org").ValueOrThrow(), token, GitHubBaseUrl());
        credential.MarkTokenInvalid(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        // Act
        TokenUsability result = credential.TokenUsability;

        // Assert
        result.ShouldBeOfType<TokenUsability.Invalid>().Token.ShouldBe(token);
    }

    [Fact]
    public void WhenTokenIsUnreadableAndInvalid_ReturnsUnreadable()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.CreateWithUnreadableToken(AccountName.Create("my-org").ValueOrThrow(), GitHubBaseUrl());
        credential.MarkTokenInvalid(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        // Act
        TokenUsability result = credential.TokenUsability;

        // Assert
        result.ShouldBeOfType<TokenUsability.Unreadable>();
    }
}
