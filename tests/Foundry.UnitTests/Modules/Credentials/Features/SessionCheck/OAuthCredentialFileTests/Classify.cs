using System.Globalization;

using Foundry.Modules.Credentials.Features.SessionCheck;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.SessionCheck.OAuthCredentialFileTests;

public sealed class Classify
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static string Credentials(string refreshToken, long expiresAtMs) =>
        $$$"""
        {"claudeAiOauth":{"accessToken":"sk-ant-oat01-x","refreshToken":"{{{refreshToken}}}","expiresAt":{{{expiresAtMs.ToString(CultureInfo.InvariantCulture)}}},"scopes":["user:inference"],"subscriptionType":"max"}}
        """;

    [Fact]
    public void WhenAccessTokenUnexpired_ReturnsActive()
    {
        // Arrange
        string json = Credentials("sk-ant-ort01-x", Now.AddHours(1).ToUnixTimeMilliseconds());

        // Act
        OAuthSessionStatus status = OAuthCredentialFile.Classify(json, Now);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.Active>();
        status.RequiresSignIn.ShouldBeFalse();
    }

    [Fact]
    public void WhenAccessTokenExpiredWithRefreshToken_ReturnsExpired()
    {
        // Arrange
        string json = Credentials("sk-ant-ort01-x", Now.AddMinutes(-1).ToUnixTimeMilliseconds());

        // Act
        OAuthSessionStatus status = OAuthCredentialFile.Classify(json, Now);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.Expired>();
        status.RequiresSignIn.ShouldBeFalse();
    }

    [Fact]
    public void WhenCliClearedTheTokensAfterInvalidGrant_ReturnsRefreshRejected()
    {
        // Arrange — the exact shape the CLI writes after an invalid_grant refresh response.
        const string json = """
            {"claudeAiOauth":{"accessToken":"","refreshToken":"","expiresAt":0,"refreshTokenExpiresAt":1793962969583,"scopes":["user:inference"],"subscriptionType":"max"}}
            """;

        // Act
        OAuthSessionStatus status = OAuthCredentialFile.Classify(json, Now);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.RefreshRejected>();
        status.RequiresSignIn.ShouldBeTrue();
    }

    [Fact]
    public void WhenRefreshTokenEmptyEvenWithFutureExpiry_ReturnsRefreshRejected()
    {
        // Arrange
        string json = Credentials("", Now.AddHours(1).ToUnixTimeMilliseconds());

        // Act
        OAuthSessionStatus status = OAuthCredentialFile.Classify(json, Now);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.RefreshRejected>();
    }

    [Fact]
    public void WhenExpiredWithoutRefreshToken_ReturnsNotRefreshable()
    {
        // Arrange
        string json = $$$"""
            {"claudeAiOauth":{"accessToken":"sk-ant-oat01-x","expiresAt":{{{Now.AddMinutes(-1).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}}}}}
            """;

        // Act
        OAuthSessionStatus status = OAuthCredentialFile.Classify(json, Now);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.NotRefreshable>();
        status.RequiresSignIn.ShouldBeTrue();
    }

    [Fact]
    public void WhenNoExpiry_ReturnsActive()
    {
        // Arrange
        const string json = """{"claudeAiOauth":{"accessToken":"sk-ant-oat01-x","refreshToken":null,"expiresAt":null}}""";

        // Act
        OAuthSessionStatus status = OAuthCredentialFile.Classify(json, Now);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.Active>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("""{"claudeAiOauth":null}""")]
    public void WhenFileOrOAuthEntryMissing_ReturnsSignedOut(string? json)
    {
        // Act
        OAuthSessionStatus status = OAuthCredentialFile.Classify(json, Now);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.SignedOut>();
        status.RequiresSignIn.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("""{"claudeAiOauth":"text"}""")]
    [InlineData("""{"claudeAiOauth":{"refreshToken":"x","expiresAt":"soon"}}""")]
    public void WhenFileMalformed_ReturnsUnreadable(string json)
    {
        // Act
        OAuthSessionStatus status = OAuthCredentialFile.Classify(json, Now);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.Unreadable>();
        status.RequiresSignIn.ShouldBeFalse();
    }
}
