namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// The hook settings Foundry sends when it registers a webhook, other than the payload URL and secret.
/// The provider clients build their request payloads from these, and <see cref="WebhookSpecStamp"/>
/// hashes the same values, so a change here re-registers every hook (ADR 0116).
/// </summary>
internal static class WebhookHookSettings
{
    internal const bool GitHubActive = true;

    internal const string GitHubContentType = "json";

    internal const string GitHubInsecureSsl = "0";

    internal const bool GitLabEnableSslVerification = true;

    /// <summary>
    /// Every trigger in <paramref name="allTriggers"/>, in order, set to <see langword="true"/> when it is
    /// in <paramref name="subscribedTriggers"/> and <see langword="false"/> otherwise.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, bool>> GitLabTriggerFlags(
        IReadOnlyList<string> allTriggers,
        IReadOnlyList<string> subscribedTriggers) =>
        [.. allTriggers.Select(t => KeyValuePair.Create(t, subscribedTriggers.Contains(t, StringComparer.Ordinal)))];

    internal static IReadOnlyList<KeyValuePair<string, bool>> GitLabTriggerFlags() =>
        GitLabTriggerFlags(GitLabWebhookEventMapper.AllTriggers, GitLabWebhookEventMapper.SubscribedTriggers);
}
