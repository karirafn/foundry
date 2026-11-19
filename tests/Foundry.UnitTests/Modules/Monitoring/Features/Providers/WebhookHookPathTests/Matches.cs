using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.WebhookHookPathTests;

public sealed class Matches
{
    private const string HookId = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("https://new.example/webhooks/github/" + HookId)]
    [InlineData("https://old.example/webhooks/github/" + HookId)]
    [InlineData("https://proxy.example/foundry/webhooks/github/" + HookId)]
    [InlineData("http://10.0.0.5:5099/webhooks/github/" + HookId)]
    public void WhenTheUrlPathEndsWithTheHookIdPath_IsTrueWhateverTheBaseUrl(string url)
    {
        WebhookHookPath sut = WebhookHookPath.For(WebhookProvider.GitHub, HookId);

        sut.Matches(url).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/webhooks/github/" + HookId)]
    [InlineData("not a url")]
    [InlineData("https://new.example/webhooks/github/ffffffffffffffffffffffffffffffff")]
    [InlineData("https://new.example/webhooks/gitlab/" + HookId)]
    [InlineData("https://new.example/webhooks/github/" + HookId + "/extra")]
    [InlineData("https://new.example/webhooks/github/0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("https://new.example/other?u=/webhooks/github/" + HookId)]
    public void WhenTheUrlDoesNotCarryTheHookIdPath_IsFalse(string? url)
    {
        WebhookHookPath sut = WebhookHookPath.For(WebhookProvider.GitHub, HookId);

        sut.Matches(url).ShouldBeFalse();
    }

    [Fact]
    public void ForGitLab_UsesTheGitLabPath()
    {
        WebhookHookPath sut = WebhookHookPath.For(WebhookProvider.GitLab, HookId);

        sut.Suffix.ShouldBe("/webhooks/gitlab/" + HookId);
        sut.Matches("https://hooks.example/webhooks/gitlab/" + HookId).ShouldBeTrue();
    }

    [Fact]
    public void TheSpecToString_MasksTheSecret()
    {
        WebhookHookSpec spec = new(
            "https://hooks.example/webhooks/github/" + HookId,
            "s3cr3t-value",
            WebhookHookPath.For(WebhookProvider.GitHub, HookId));

        spec.ToString().ShouldNotContain("s3cr3t-value");
    }
}
