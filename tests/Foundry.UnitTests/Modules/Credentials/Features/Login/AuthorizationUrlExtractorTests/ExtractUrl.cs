using Foundry.Modules.Credentials.Features.Login;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.AuthorizationUrlExtractorTests;

public sealed class ExtractUrl
{
    [Fact]
    public void WhenLineContainsOAuthUrl_ReturnsUrl()
    {
        // Arrange
        string line = "If the browser didn't open, visit: https://claude.com/cai/oauth/authorize?code=true&client_id=abc&response_type=code&state=xyz";

        // Act
        string? result = AuthorizationUrlExtractor.Extract(line);

        // Assert
        result.ShouldBe("https://claude.com/cai/oauth/authorize?code=true&client_id=abc&response_type=code&state=xyz");
    }

    [Fact]
    public void WhenLineHasNoUrl_ReturnsNull()
    {
        // Arrange
        string line = "Starting Claude authentication process...";

        // Act
        string? result = AuthorizationUrlExtractor.Extract(line);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenLineHasUrlWithoutOauth_ReturnsNull()
    {
        // Arrange
        string line = "Visit https://example.com for more information";

        // Act
        string? result = AuthorizationUrlExtractor.Extract(line);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenMultipleLinesProvided_ReturnsFirstOAuthUrl()
    {
        // Arrange
        string[] lines =
        [
            "Starting login...",
            "If the browser didn't open, visit: https://claude.com/cai/oauth/authorize?code=true&client_id=first&state=aaa",
            "If the browser didn't open, visit: https://claude.com/cai/oauth/authorize?code=true&client_id=second&state=bbb",
        ];

        // Act
        string? result = null;

        foreach (string line in lines)
        {
            result = AuthorizationUrlExtractor.Extract(line);
            if (result is not null)
            {
                break;
            }
        }

        // Assert
        result.ShouldBe("https://claude.com/cai/oauth/authorize?code=true&client_id=first&state=aaa");
    }
}
