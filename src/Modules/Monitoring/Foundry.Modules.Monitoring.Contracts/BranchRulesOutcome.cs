namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The per-request result of an attempt to apply Foundry's default-branch rules when a repository
/// is added. Returned in the response only — never persisted.
/// </summary>
/// <param name="Kind">One of the <c>*Kind</c> constants.</param>
/// <param name="Reason">One of the <c>*Reason</c> constants; set only when <paramref name="Kind"/> is
/// <see cref="SkippedKind"/> or <see cref="FailedKind"/>.</param>
/// <param name="SettingsUrl">The provider's branch-rules settings page for the repository; set
/// whenever the rules were not applied by this request.</param>
/// <param name="Detail">A human-readable explanation; set only when <paramref name="Kind"/> is
/// <see cref="FailedKind"/>.</param>
public sealed record BranchRulesOutcome(
    string Kind,
    string? Reason = null,
    string? SettingsUrl = null,
    string? Detail = null)
{
    /// <summary>Foundry wrote the rules and the re-read confirmed them.</summary>
    public const string AppliedKind = "applied";

    /// <summary>The rules were already in place; nothing was written.</summary>
    public const string AlreadyPresentKind = "already-present";

    /// <summary>The operator opted out of applying branch rules.</summary>
    public const string NotRequestedKind = "not-requested";

    /// <summary>Foundry did not attempt the write; see <see cref="Reason"/>.</summary>
    public const string SkippedKind = "skipped";

    /// <summary>Foundry attempted the write and it did not take effect; see <see cref="Reason"/>.</summary>
    public const string FailedKind = "failed";

    /// <summary>The account token cannot push to the repository (or is missing/unreadable).</summary>
    public const string AccessFailureReason = "access-failure";

    /// <summary>Eligibility could not be determined, so the need for rules is unknown.</summary>
    public const string UnverifiableReason = "unverifiable";

    /// <summary>The account has no readable branch-rules token.</summary>
    public const string NoRulesTokenReason = "no-rules-token";

    /// <summary>The provider rejected the write with 403.</summary>
    public const string InsufficientPermissionReason = "insufficient-permission";

    /// <summary>Any other provider failure, including rules written but not effective.</summary>
    public const string ProviderErrorReason = "provider-error";
}
