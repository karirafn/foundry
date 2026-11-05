using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>The poll pass a <see cref="Nudge"/> asks <see cref="Polling.RepositoryPoller"/> to run.</summary>
internal enum NudgePass
{
    /// <summary>Issue listing: detection, detail change and complete-listing untrack.</summary>
    IssueListing,

    /// <summary>Issue listing followed by the dependency pass on the same provider instance.</summary>
    IssueListingAndDependencies,

    /// <summary>Dependency pass alone (follow-up after a nudged listing detected new issues).</summary>
    Dependencies,

    /// <summary>Review pass for one issue.</summary>
    Review,

    /// <summary>Checks pass for one issue.</summary>
    Checks,

    /// <summary>The branch-rules eligibility step.</summary>
    BranchRules,

    /// <summary>The post-run rebase step for one issue (ADR 0087).</summary>
    Rebase,
}

/// <summary>
/// Identifies one nudgeable pass: <c>(repository, pass, issue?)</c>. Nudges with the same key
/// coalesce and share one rate cap (ADR 0085).
/// </summary>
internal readonly record struct NudgeKey(MonitoredRepositoryId RepositoryId, NudgePass Pass, int? IssueNumber = null);

/// <summary>
/// A request to run one poll pass now, or not before <see cref="NotBefore"/> (deadline nudges).
/// A nudge never changes domain state itself — it only runs an existing <see cref="Polling.RepositoryPoller"/> pass.
/// </summary>
internal readonly record struct Nudge(NudgeKey Key, DateTimeOffset? NotBefore = null);
