using System.Globalization;

using Foundry.Modules.Credentials.Features.SessionCheck;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.SessionCheck.OAuthCredentialFileTests;

public sealed class ReadActiveAccessToken
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static string Credentials(string accessToken, long expiresAtMs) =>
        $$$"""
        {"claudeAiOauth":{"accessToken":"{{{accessToken}}}","refreshToken":"sk-ant-ort01-x","expiresAt":{{{expiresAtMs.ToString(CultureInfo.InvariantCulture)}}},"scopes":["user:inference"]}}
        """;

    [Fact]
    public void WhenSessionIsActive_ReturnsTheAccessToken()
    {
        // Arrange
        string json = Credentials("sk-ant-oat01-live", Now.AddHours(1).ToUnixTimeMilliseconds());

        // Act
        string? token = OAuthCredentialFile.ReadActiveAccessToken(json, Now);

        // Assert
        token.ShouldBe("sk-ant-oat01-live");
    }

    [Fact]
    public void WhenAccessTokenExpired_ReturnsNull()
    {
        // Arrange
        string json = Credentials("sk-ant-oat01-stale", Now.AddMinutes(-1).ToUnixTimeMilliseconds());

        // Act
        string? token = OAuthCredentialFile.ReadActiveAccessToken(json, Now);

        // Assert
        token.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"claudeAiOauth":null}""")]
    public void WhenNoSessionIsStored_ReturnsNull(string? json)
    {
        // Act
        string? token = OAuthCredentialFile.ReadActiveAccessToken(json, Now);

        // Assert
        token.ShouldBeNull();
    }

    [Fact]
    public void WhenActiveSessionHasAnEmptyAccessToken_ReturnsNull()
    {
        // Arrange
        string json = Credentials(string.Empty, Now.AddHours(1).ToUnixTimeMilliseconds());

        // Act
        string? token = OAuthCredentialFile.ReadActiveAccessToken(json, Now);

        // Assert
        token.ShouldBeNull();
    }
}
