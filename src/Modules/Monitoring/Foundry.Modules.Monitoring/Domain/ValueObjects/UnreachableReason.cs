namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

public enum UnreachableReason
{
    /// <summary>The write probe was never successfully attempted (initial state).</summary>
    NeverProbed = 0,

    /// <summary>The GitHub API rate limit was exhausted during the write probe.</summary>
    RateLimited = 1,

    /// <summary>The branch-rules GET failed, preventing a definitive eligibility decision.</summary>
    BranchRulesUnavailable = 2,
}
