using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Features.Webhooks.Delivery;
using Foundry.Modules.Monitoring.Features.Webhooks.Registration;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.Registration.WebhookSpecStampTests;

public sealed class For
{
    private const string PublicBaseUrl = "https://hooks.example.com";

    [Fact]
    public void ForTheSameProviderAndBaseUrl_IsStable()
    {
        // Act
        string first = WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl);
        string second = WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl);

        // Assert
        first.ShouldBe(second);
    }

    [Fact]
    public void IsLowercaseHexSha256()
    {
        // Act
        string stamp = WebhookSpecStamp.For(WebhookProvider.GitLab, PublicBaseUrl);

        // Assert
        stamp.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ForGitHubAndGitLab_Differs()
    {
        // Act
        string gitHub = WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl);
        string gitLab = WebhookSpecStamp.For(WebhookProvider.GitLab, PublicBaseUrl);

        // Assert
        gitHub.ShouldNotBe(gitLab);
    }

    [Fact]
    public void ForAnotherBaseUrl_Differs()
    {
        // Act
        string current = WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl);
        string moved = WebhookSpecStamp.For(WebhookProvider.GitHub, "https://other.example.com");

        // Assert
        current.ShouldNotBe(moved);
    }

    [Fact]
    public void ForGitHub_IsBuiltFromTheSubscribedEvents()
    {
        // Act
        string current = WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl);
        string fromSubscribed = WebhookSpecStamp.ForGitHub(GitHubWebhookEventMapper.SubscribedEvents, PublicBaseUrl);

        // Assert
        current.ShouldBe(fromSubscribed);
    }

    [Fact]
    public void ForGitHub_WhenAnEventIsAdded_Differs()
    {
        // Arrange
        string[] withExtraEvent = [.. GitHubWebhookEventMapper.SubscribedEvents, "release"];

        // Act
        string current = WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl);
        string changed = WebhookSpecStamp.ForGitHub(withExtraEvent, PublicBaseUrl);

        // Assert
        current.ShouldNotBe(changed);
    }

    [Fact]
    public void ForGitHub_WhenAnEventIsRemoved_Differs()
    {
        // Arrange
        string[] withoutPush = [.. GitHubWebhookEventMapper.SubscribedEvents.Where(e => e != "push")];

        // Act
        string current = WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl);
        string changed = WebhookSpecStamp.ForGitHub(withoutPush, PublicBaseUrl);

        // Assert
        current.ShouldNotBe(changed);
    }

    [Fact]
    public void ForGitLab_IsBuiltFromTheSubscribedTriggers()
    {
        // Act
        string current = WebhookSpecStamp.For(WebhookProvider.GitLab, PublicBaseUrl);
        string fromSubscribed = WebhookSpecStamp.ForGitLab(
            GitLabWebhookEventMapper.AllTriggers,
            GitLabWebhookEventMapper.SubscribedTriggers,
            PublicBaseUrl);

        // Assert
        current.ShouldBe(fromSubscribed);
    }

    [Fact]
    public void ForGitLab_WhenATriggerIsSubscribed_Differs()
    {
        // Arrange
        string[] withTagPush = [.. GitLabWebhookEventMapper.SubscribedTriggers, "tag_push_events"];

        // Act
        string current = WebhookSpecStamp.For(WebhookProvider.GitLab, PublicBaseUrl);
        string changed = WebhookSpecStamp.ForGitLab(GitLabWebhookEventMapper.AllTriggers, withTagPush, PublicBaseUrl);

        // Assert
        current.ShouldNotBe(changed);
    }

    [Fact]
    public void ForGitLab_WhenATriggerIsUnsubscribed_Differs()
    {
        // Arrange
        string[] withoutPush = [.. GitLabWebhookEventMapper.SubscribedTriggers.Where(t => t != "push_events")];

        // Act
        string current = WebhookSpecStamp.For(WebhookProvider.GitLab, PublicBaseUrl);
        string changed = WebhookSpecStamp.ForGitLab(GitLabWebhookEventMapper.AllTriggers, withoutPush, PublicBaseUrl);

        // Assert
        current.ShouldNotBe(changed);
    }
}
