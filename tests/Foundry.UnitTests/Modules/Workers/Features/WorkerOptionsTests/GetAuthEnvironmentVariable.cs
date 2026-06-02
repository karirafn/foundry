using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerOptionsTests;

public sealed class GetAuthEnvironmentVariable
{
    [Fact]
    public void WhenApiKeySet_ReturnsAnthropicApiKeyVar()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = "sk-ant-test-key",
            OAuthToken = string.Empty,
        };

        // Act
        KeyValuePair<string, string> result = options.GetAuthEnvironmentVariable();

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Key.ShouldBe("ANTHROPIC_API_KEY"),
            () => result.Value.ShouldBe("sk-ant-test-key"));
    }

    [Fact]
    public void WhenOAuthTokenSet_ReturnsClaudeCodeOAuthTokenVar()
    {
        // Arrange
        WorkerOptions options = new()
        {
            ApiKey = string.Empty,
            OAuthToken = "oauth-test-token",
        };

        // Act
        KeyValuePair<string, string> result = options.GetAuthEnvironmentVariable();

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Key.ShouldBe("CLAUDE_CODE_OAUTH_TOKEN"),
            () => result.Value.ShouldBe("oauth-test-token"));
    }
}
