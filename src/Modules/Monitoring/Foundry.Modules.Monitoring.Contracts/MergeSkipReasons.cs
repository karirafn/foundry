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

    /// <summary>
    /// The pull request kept falling behind the default branch: its merge was refused as behind again after every
    /// catch-up round its queue entry allows (ADR 0111).
    /// </summary>
    public const string FellBehind = "fell_behind";

    /// <summary>A catch-up rebase could not bring the pull request up to date with the default branch (ADR 0111).</summary>
    public const string CatchUpFailed = "catch_up_failed";
}
