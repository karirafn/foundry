using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Polling;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>The provider a repository's webhook is registered on.</summary>
internal enum WebhookProvider
{
    GitHub,
    GitLab,
}

internal static class WebhookProviders
{
    /// <summary>The webhook provider for the credential that covers a repository.</summary>
    public static WebhookProvider? From(Credential? credential) => credential switch
    {
        GitHubCredential => WebhookProvider.GitHub,
        GitLabCredential => WebhookProvider.GitLab,
        _ => null,
    };

    /// <summary>
    /// The poll change categories the provider has a webhook event for — only those can be a
    /// <em>missed change</em> (ADR 0085). GitLab has no event for issue links or protected branches, so
    /// branch-rule changes found by a backstop poll never move a GitLab hook to <c>Silent</c>; they are
    /// picked up within the Backstop Interval instead. Dependency changes are never a category
    /// (<see cref="PollChanges"/>), so they never count on either provider.
    /// </summary>
    public static PollChanges MissedChangeCategories(WebhookProvider provider) => provider switch
    {
        WebhookProvider.GitHub => PollChanges.IssueListing | PollChanges.Review | PollChanges.Checks | PollChanges.BranchRules,
        WebhookProvider.GitLab => PollChanges.IssueListing | PollChanges.Review | PollChanges.Checks,
        _ => PollChanges.None,
    };
}
