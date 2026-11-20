using Foundry.Modules.Settings.Contracts.Queries;

namespace Foundry.Modules.Monitoring.Features.Webhooks.Ingress;

/// <summary>
/// The effective webhook ingress state: configured once a Webhook Public Base URL (Global Settings) is
/// stored. The webhook port itself is always bound (ADR 0116).
/// </summary>
internal sealed record WebhookIngressState(bool IsConfigured, string? PublicBaseUrl, TimeSpan BackstopInterval)
{
    internal const string GitHubPathPrefix = "/webhooks/github/";
    internal const string GitLabPathPrefix = "/webhooks/gitlab/";

    /// <summary>The payload URL to register on <paramref name="provider"/>; <see langword="null"/> when not configured.</summary>
    internal string? PayloadUrl(WebhookProvider provider, string hookId)
    {
        if (!IsConfigured || PublicBaseUrl is not { } baseUrl)
        {
            return null;
        }

        string prefix = provider == WebhookProvider.GitLab ? GitLabPathPrefix : GitHubPathPrefix;
        return $"{baseUrl}{prefix}{hookId}";
    }
}

internal sealed class WebhookIngress(IWebhookSettingsQueries settingsQueries)
{
    public async Task<WebhookIngressState> GetStateAsync(CancellationToken cancellationToken)
    {
        WebhookSettingsSnapshot settings = await settingsQueries.GetWebhookSettingsAsync(cancellationToken);
        bool configured = !string.IsNullOrEmpty(settings.PublicBaseUrl);
        return new WebhookIngressState(configured, settings.PublicBaseUrl, settings.BackstopInterval);
    }
}
