namespace Foundry.Modules.Monitoring.Contracts;

public sealed record RepositoryEligibilityInfo(
    string Status,
    IReadOnlyList<EligibilityViolationInfo> Violations,
    string? Reason)
{
    /// <summary>The write probe was never successfully attempted (initial state).</summary>
    public const string NeverProbedReason = "never-probed";

    /// <summary>The GitHub API rate limit was exhausted during the write probe.</summary>
    public const string RateLimitedReason = "rate-limited";

    /// <summary>The branch-rules GET failed, preventing a definitive eligibility decision.</summary>
    public const string BranchRulesUnavailableReason = "branch-rules-unavailable";

    /// <summary>The provider refused the account token when a run used it; write access is re-checked on the next poll.</summary>
    public const string AccessRefusedReason = "access-refused";
}
