namespace Foundry.Modules.Monitoring.Features.Providers.Merge;

/// <summary>
/// The provider's answer to merging a pull request pinned to the head its checks gate verified (ADR 0105).
/// Transient failures (5xx, timeouts, rate limits) are a <c>Result</c> failure instead; every variant here is a
/// definite answer.
/// </summary>
internal abstract record MergeRequestOutcome
{
    private MergeRequestOutcome()
    {
    }

    /// <summary>The provider merged the pull request.</summary>
    public sealed record Merged : MergeRequestOutcome;

    /// <summary>The pull request head no longer matches the expected head — a push landed after the checks verdict.</summary>
    public sealed record HeadMoved : MergeRequestOutcome;

    /// <summary>
    /// The provider refuses to merge (conflicts, unmet approvals, a draft, already merged or closed) or answered
    /// in a way the classifier does not recognise; <see cref="Reason"/> says why.
    /// </summary>
    public sealed record NotMergeable(string Reason) : MergeRequestOutcome
    {
        /// <summary>The repository allows none of the merge methods Foundry can use.</summary>
        public const string NoMergeMethodAllowed = "no_merge_method_allowed";

        /// <summary>The provider answered with a response the classifier does not recognise; fails open to review.</summary>
        public const string UnrecognisedError = "unrecognised_error";
    }

    /// <summary>The token may not merge this pull request.</summary>
    public sealed record Unauthorized : MergeRequestOutcome;

    /// <summary>The provider has no merge implementation.</summary>
    public sealed record Unsupported : MergeRequestOutcome;
}
