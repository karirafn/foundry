using Foundry.Modules.Settings.Contracts.Queries;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// The effective webhook ingress state: configured only when both the webhook port (host
/// configuration) and the Webhook Public Base URL (Global Settings) are set.
/// </summary>
internal sealed record WebhookIngressState(bool IsConfigured, string? PublicBaseUrl, TimeSpan BackstopInterval)
{
    internal const string GitHubPathPrefix = "/webhooks/github/";

    internal string? GitHubPayloadUrl(string hookId) =>
        IsConfigured && PublicBaseUrl is { } baseUrl ? $"{baseUrl}{GitHubPathPrefix}{hookId}" : null;
}

internal sealed class WebhookIngress(IOptions<WebhookIngressOptions> options, IWebhookSettingsQueries settingsQueries)
{
    public async Task<WebhookIngressState> GetStateAsync(CancellationToken cancellationToken)
    {
        WebhookSettingsSnapshot settings = await settingsQueries.GetWebhookSettingsAsync(cancellationToken);
        bool configured = options.Value.Port is not null && !string.IsNullOrEmpty(settings.PublicBaseUrl);
        return new WebhookIngressState(configured, settings.PublicBaseUrl, settings.BackstopInterval);
    }
}
