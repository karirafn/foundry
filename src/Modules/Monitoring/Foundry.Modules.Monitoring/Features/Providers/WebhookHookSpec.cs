namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// The repository webhook Foundry registers: the payload URL, the repository's secret, and any
/// earlier payload URLs (a changed Webhook Public Base URL) whose hook should be re-pointed rather
/// than duplicated. The subscribed events are provider-specific and chosen by the provider.
/// </summary>
internal sealed record WebhookHookSpec(
    string PayloadUrl,
    string Secret,
    IReadOnlyList<string> PreviousPayloadUrls)
{
    /// <summary>Whether <paramref name="url"/> addresses this hook (current or a previous payload URL).</summary>
    public bool Matches(string? url) =>
        url is not null
        && (string.Equals(url, PayloadUrl, StringComparison.Ordinal)
            || PreviousPayloadUrls.Contains(url, StringComparer.Ordinal));

    /// <summary>
    /// Masks the secret in diagnostics: a record's synthesized <c>ToString</c> would otherwise print it.
    /// </summary>
    public override string ToString() =>
        $"WebhookHookSpec {{ PayloadUrl = {PayloadUrl}, Secret = ***, PreviousPayloadUrls = [{string.Join(", ", PreviousPayloadUrls)}] }}";
}

/// <summary>What <see cref="IIssueProvider.UpsertWebhookAsync"/> did on the provider.</summary>
internal enum WebhookUpsertKind
{
    /// <summary>No hook matched the payload URL, so one was created.</summary>
    Created,

    /// <summary>A hook with a matching payload URL was updated in place.</summary>
    Updated,
}
