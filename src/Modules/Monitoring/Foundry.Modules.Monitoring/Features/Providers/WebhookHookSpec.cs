using Foundry.Modules.Monitoring.Features.Webhooks;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// The payload-URL path suffix that identifies a repository's hook on the provider:
/// <c>/webhooks/github/{hookId}</c> or <c>/webhooks/gitlab/{hookId}</c>. A provider hook belongs to the
/// repository when its URL path ends with this suffix, whatever the base URL (ADR 0116): a hook left at
/// an older Webhook Public Base URL, or added by hand, is adopted rather than duplicated.
/// </summary>
internal sealed record WebhookHookPath
{
    private WebhookHookPath(string suffix) => Suffix = suffix;

    public string Suffix { get; }

    public static WebhookHookPath For(WebhookProvider provider, string hookId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookId);
        string prefix = provider == WebhookProvider.GitLab
            ? WebhookIngressState.GitLabPathPrefix
            : WebhookIngressState.GitHubPathPrefix;
        return new WebhookHookPath(prefix + hookId);
    }

    /// <summary>
    /// Whether <paramref name="url"/> is an absolute http(s) URI whose path ends, ordinal, with
    /// <see cref="Suffix"/>. The scheme check keeps a bare path from parsing as a <c>file:</c> URI on Unix.
    /// </summary>
    public bool Matches(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.AbsolutePath.EndsWith(Suffix, StringComparison.Ordinal);

    public override string ToString() => Suffix;
}

/// <summary>
/// The repository webhook Foundry registers: the payload URL, the repository's secret, and the hook-id
/// path that identifies the repository's existing hooks. The subscribed events are provider-specific and
/// chosen by the provider.
/// </summary>
internal sealed record WebhookHookSpec(
    string PayloadUrl,
    string Secret,
    WebhookHookPath HookPath)
{
    /// <summary>Whether <paramref name="url"/> addresses one of this repository's hooks.</summary>
    public bool Matches(string? url) => HookPath.Matches(url);

    /// <summary>
    /// Masks the secret in diagnostics: a record's synthesized <c>ToString</c> would otherwise print it.
    /// </summary>
    public override string ToString() =>
        $"WebhookHookSpec {{ PayloadUrl = {PayloadUrl}, Secret = ***, HookPath = {HookPath} }}";
}

/// <summary>What <see cref="IIssueProvider.UpsertWebhookAsync"/> did on the provider.</summary>
internal enum WebhookUpsertKind
{
    /// <summary>No hook matched the repository's hook id, so one was created.</summary>
    Created,

    /// <summary>A hook carrying the repository's hook id was updated in place.</summary>
    Updated,
}

/// <summary>
/// What <see cref="IIssueProvider.UpsertWebhookAsync"/> did on the provider, and the provider's id of the
/// hook it created or updated — the id <see cref="IIssueProvider.TestWebhookAsync"/> addresses.
/// </summary>
internal sealed record WebhookUpsert(WebhookUpsertKind Kind, long ProviderHookId);
