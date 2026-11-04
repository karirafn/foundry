namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The per-request result of an attempt to register (create or update) a repository's webhook on
/// the provider with the account's server-side token. Returned in responses only — never persisted;
/// the lasting effect is the repository's Webhook Status.
/// </summary>
/// <param name="Kind">One of the <c>*Kind</c> constants.</param>
/// <param name="Reason">One of the <c>*Reason</c> constants; set only when <paramref name="Kind"/> is
/// <see cref="SkippedKind"/> or <see cref="FailedKind"/>.</param>
/// <param name="Detail">A human-readable explanation; set only when <paramref name="Kind"/> is
/// <see cref="FailedKind"/>.</param>
public sealed record WebhookRegistrationOutcome(
    string Kind,
    string? Reason = null,
    string? Detail = null)
{
    /// <summary>Foundry created a new hook on the provider.</summary>
    public const string CreatedKind = "created";

    /// <summary>Foundry updated an existing hook with the same payload URL in place.</summary>
    public const string UpdatedKind = "updated";

    /// <summary>Foundry made no provider call; see <see cref="Reason"/>.</summary>
    public const string SkippedKind = "skipped";

    /// <summary>The provider rejected the registration; see <see cref="Reason"/>.</summary>
    public const string FailedKind = "failed";

    /// <summary>Webhook ingress is not configured (no webhook port or no public base URL).</summary>
    public const string NotConfiguredReason = "not-configured";

    /// <summary>The account has no server-side (branch-rules) token.</summary>
    public const string NoServerTokenReason = "no-server-token";

    /// <summary>The account's server-side token can no longer be decrypted.</summary>
    public const string ServerTokenUnreadableReason = "server-token-unreadable";

    /// <summary>The token lacks the permission to manage webhooks (GitHub 403/404, GitLab 403).</summary>
    public const string InsufficientPermissionReason = "insufficient-permission";

    /// <summary>Any other provider failure.</summary>
    public const string ProviderErrorReason = "provider-error";

    /// <summary>Whether the hook now exists on the provider (created or updated).</summary>
    public bool IsRegistered => Kind is CreatedKind or UpdatedKind;
}
