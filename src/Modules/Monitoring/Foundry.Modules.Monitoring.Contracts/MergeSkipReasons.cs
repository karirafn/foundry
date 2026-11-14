namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The reasons Foundry's own merge of a pull request resolves as <c>skipped</c> (ADR 0105). Every one sends the
/// issue to review, where a person decides.
/// </summary>
public static class MergeSkipReasons
{
    /// <summary>The provider refused the merge: conflicts, a branch rule, missing approvals, or no merge implementation.</summary>
    public const string NotMergeable = "not_mergeable";

    /// <summary>The account's token may not merge the pull request.</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>New commits reached the pull request after its checks verdict, so the pinned merge was refused.</summary>
    public const string HeadMoved = "head_moved";

    /// <summary>Transient provider failures (5xx, timeouts, rate limits) exhausted the retry budget.</summary>
    public const string ProviderError = "provider_error";
}
