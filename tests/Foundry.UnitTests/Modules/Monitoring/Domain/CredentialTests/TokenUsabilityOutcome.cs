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
        GitHubCredential credential = GitHubCredential.Create("my-org", token, GitHubBaseUrl());

        // Act
        TokenUsability result = credential.TokenUsability;

        // Assert
        result.ShouldBeOfType<TokenUsability.Usable>().Token.ShouldBe(token);
    }

    [Fact]
    public void WhenTokenIsUnreadable_ReturnsUnreadable()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.CreateWithUnreadableToken("my-org", GitHubBaseUrl());

        // Act
        TokenUsability result = credential.TokenUsability;

        // Assert
        result.ShouldBeOfType<TokenUsability.Unreadable>();
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsAbsent()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", null, GitHubBaseUrl());

        // Act
        TokenUsability result = credential.TokenUsability;

        // Assert
        result.ShouldBeOfType<TokenUsability.Absent>();
    }
}
