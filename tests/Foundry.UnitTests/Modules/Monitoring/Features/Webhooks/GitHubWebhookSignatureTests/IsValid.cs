using System.Text;

using Foundry.Modules.Monitoring.Features.Webhooks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.GitHubWebhookSignatureTests;

public sealed class IsValid
{
    private const string Secret = "It's a Secret to Everybody";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("Hello, World!");

    [Fact]
    public void WhenSignatureMatchesGitHubDocumentedExample_ReturnsTrue()
    {
        // Arrange — the example from GitHub's "Validating webhook deliveries" documentation.
        const string signature = "sha256=757107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e17";

        // Act & Assert
        GitHubWebhookSignature.IsValid(Body, signature, Secret).ShouldBeTrue();
    }

    [Fact]
    public void WhenComputedForBody_ReturnsTrue()
    {
        // Act & Assert
        GitHubWebhookSignature.IsValid(Body, GitHubWebhookSignature.Compute(Body, Secret), Secret).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=757107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e17")]
    [InlineData("sha256=757107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e18")]
    [InlineData("sha256=zz7107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e17")]
    [InlineData("sha256=757107")]
    public void WhenSignatureMissingOrWrong_ReturnsFalse(string? signature)
    {
        // Act & Assert
        GitHubWebhookSignature.IsValid(Body, signature, Secret).ShouldBeFalse();
    }

    [Fact]
    public void WhenSecretDiffers_ReturnsFalse()
    {
        // Act & Assert
        GitHubWebhookSignature.IsValid(Body, GitHubWebhookSignature.Compute(Body, Secret), "other").ShouldBeFalse();
    }

    [Fact]
    public void WhenSecretMissing_ReturnsFalse()
    {
        // Act & Assert
        GitHubWebhookSignature.IsValid(Body, GitHubWebhookSignature.Compute(Body, Secret), null).ShouldBeFalse();
    }
}
