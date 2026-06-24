using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.SecretRedactorTests;

public sealed class Redact
{
    [Fact]
    public void WhenInputHasNoSecrets_OutputIsUnchanged()
    {
        // Arrange
        string clean = "Cloning into '/workspace'...\nDone.";

        // Act
        string result = SecretRedactor.Redact(clean);

        // Assert
        result.ShouldBe(clean);
    }

    [Fact]
    public void WhenInputContainsHttpsUrlWithUserinfo_UserinfoIsRedacted()
    {
        // Arrange
        string output = "remote: Enumerating objects...\nhttps://glpat-abcXYZ@gitlab.example.com/owner/repo.git";

        // Act
        string result = SecretRedactor.Redact(output);

        // Assert
        result.ShouldContain("https://***@gitlab.example.com");
        result.ShouldNotContain("glpat-abcXYZ@");
    }

    [Theory]
    [InlineData("sk-ant-api03-abcDEF123xyz")]
    [InlineData("sk-ant-api03-abc123-DEF456_xyz")]
    public void WhenInputContainsAnthropicApiKey_KeyIsMasked(string token)
    {
        // Arrange
        string output = $"ANTHROPIC_API_KEY={token}";

        // Act
        string result = SecretRedactor.Redact(output);

        // Assert
        result.ShouldNotContain(token);
        result.ShouldContain("***");
    }

    [Theory]
    [InlineData("glpat-abc123DEF")]
    [InlineData("ghp_abcDEF123xyz")]
    [InlineData("github_pat_abc123DEFxyz")]
    [InlineData("gho_abc123DEFxyz")]
    public void WhenInputContainsKnownTokenShape_TokenIsMasked(string token)
    {
        // Arrange
        string output = $"fatal: repository 'https://example.com/repo.git' not found (token={token})";

        // Act
        string result = SecretRedactor.Redact(output);

        // Assert
        result.ShouldNotContain(token);
        result.ShouldContain("***");
    }

    [Fact]
    public void WhenInputContainsOauth2CloneUrl_UserinfoIsRedacted()
    {
        // Arrange — the oauth2@ clone URL pattern the new entrypoint.sh produces
        string output = "Cloning into '/workspace'...\nhttps://oauth2@gitlab.example.com/owner/repo.git\nDone.";

        // Act
        string result = SecretRedactor.Redact(output);

        // Assert — the oauth2 username token is stripped; host path is preserved
        result.ShouldNotContain("oauth2@");
        result.ShouldContain("https://***@gitlab.example.com");
    }
}
