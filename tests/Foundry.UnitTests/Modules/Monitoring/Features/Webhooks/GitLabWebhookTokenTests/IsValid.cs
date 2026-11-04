using Foundry.Modules.Monitoring.Features.Webhooks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.GitLabWebhookTokenTests;

public sealed class IsValid
{
    private const string Secret = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void WhenTokenEqualsSecret_ReturnsTrue()
    {
        GitLabWebhookToken.IsValid(Secret, Secret).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdee")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    public void WhenTokenMissingOrDifferent_ReturnsFalse(string? token)
    {
        GitLabWebhookToken.IsValid(token, Secret).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WhenRepositoryHasNoSecret_ReturnsFalse(string? secret)
    {
        GitLabWebhookToken.IsValid(secret, secret).ShouldBeFalse();
    }
}
