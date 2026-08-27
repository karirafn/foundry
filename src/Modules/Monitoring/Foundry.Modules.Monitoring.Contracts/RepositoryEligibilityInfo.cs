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
}
