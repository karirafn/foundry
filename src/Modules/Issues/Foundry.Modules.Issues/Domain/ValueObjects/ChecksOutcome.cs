using System.Diagnostics;

using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Closed union recording why an issue left the checks-pending gate and moved into review.
/// </summary>
public abstract record ChecksOutcome
{
    private ChecksOutcome()
    {
    }

    /// <summary>
    /// A stable string token for each variant, suitable for persistence and wire encoding.
    /// </summary>
    public string Discriminator => this switch
    {
        Passed => "passed",
        NoneDetected => "none_detected",
        Unfinished => "unfinished",
        StillFailing => "still_failing",
        InfrastructureFailed => "infrastructure_failed",
        Unreadable => "unreadable",
        _ => throw new UnreachableException($"Unknown {nameof(ChecksOutcome)} variant: {GetType().Name}"),
    };

    /// <summary>All detected checks passed.</summary>
    public sealed record Passed : ChecksOutcome;

    /// <summary>No checks were detected after the grace window elapsed.</summary>
    public sealed record NoneDetected : ChecksOutcome;

    /// <summary>Checks did not complete within the upper-bound timeout.</summary>
    public sealed record Unfinished : ChecksOutcome;

    /// <summary>Checks completed but one or more were still failing after the fix budget was exhausted.</summary>
    public sealed record StillFailing(IReadOnlyList<FailingCheck> FailingChecks) : ChecksOutcome;

    /// <summary>
    /// Checks failed, but every failing check failed before running its script (e.g. GitLab <c>ci_quota_exceeded</c>):
    /// CI couldn't run, so no fix run was started and no fix attempt was spent. A person retries the pipeline on the
    /// provider; the review pass picks up the green retry on the same head (issue #969, ADR 0108).
    /// </summary>
    public sealed record InfrastructureFailed(IReadOnlyList<FailingCheck> FailingChecks) : ChecksOutcome;

    /// <summary>The provider returned 401/403; check results could not be read.</summary>
    public sealed record Unreadable : ChecksOutcome;
}
