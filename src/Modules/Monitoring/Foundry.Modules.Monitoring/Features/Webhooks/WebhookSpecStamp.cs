using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// A lowercase hex SHA-256 over a canonical, ordered text of the provider, the hook settings Foundry
/// sends (<see cref="WebhookHookSettings"/> and the subscribed events or triggers) and the Webhook Public
/// Base URL. A repository registered with a different stamp is stale, and the scheduled poll registers
/// it again (ADR 0116). The secret is never part of it.
/// </summary>
internal static class WebhookSpecStamp
{
    public static string For(WebhookProvider provider, string publicBaseUrl) => provider switch
    {
        WebhookProvider.GitHub => ForGitHub(GitHubWebhookEventMapper.SubscribedEvents, publicBaseUrl),
        WebhookProvider.GitLab => ForGitLab(
            GitLabWebhookEventMapper.AllTriggers,
            GitLabWebhookEventMapper.SubscribedTriggers,
            publicBaseUrl),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unhandled webhook provider."),
    };

    internal static string ForGitHub(IReadOnlyList<string> subscribedEvents, string publicBaseUrl)
    {
        StringBuilder text = new();
        AppendLine(text, "provider", "github");
        AppendLine(text, "active", WebhookHookSettings.GitHubActive);
        AppendLine(text, "content_type", WebhookHookSettings.GitHubContentType);
        AppendLine(text, "insecure_ssl", WebhookHookSettings.GitHubInsecureSsl);
        AppendLine(text, "events", string.Join(',', subscribedEvents));
        AppendLine(text, "public_base_url", publicBaseUrl);
        return Hash(text);
    }

    internal static string ForGitLab(
        IReadOnlyList<string> allTriggers,
        IReadOnlyList<string> subscribedTriggers,
        string publicBaseUrl)
    {
        StringBuilder text = new();
        AppendLine(text, "provider", "gitlab");
        AppendLine(text, "enable_ssl_verification", WebhookHookSettings.GitLabEnableSslVerification);
        foreach (KeyValuePair<string, bool> flag in WebhookHookSettings.GitLabTriggerFlags(allTriggers, subscribedTriggers))
        {
            AppendLine(text, flag.Key, flag.Value);
        }

        AppendLine(text, "public_base_url", publicBaseUrl);
        return Hash(text);
    }

    private static void AppendLine(StringBuilder text, string key, bool value) =>
        AppendLine(text, key, value ? "true" : "false");

    private static void AppendLine(StringBuilder text, string key, string value) =>
        text.Append(CultureInfo.InvariantCulture, $"{key}={value}\n");

    private static string Hash(StringBuilder text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
}
