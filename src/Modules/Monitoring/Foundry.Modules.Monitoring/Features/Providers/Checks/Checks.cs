using System.Diagnostics;

using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.Checks;

/// <summary>
/// The raw provider check verdict for a pull request at a point in time.
/// Distinct from <c>ChecksOutcome</c> in the Issues module, which records the final gate result.
/// The poller maps <see cref="Checks"/> to <c>ChecksOutcome</c> after applying grace windows and budgets.
/// </summary>
internal abstract record Checks
{
    private Checks()
    {
    }

    /// <summary>All detected checks passed.</summary>
    internal sealed record Green : Checks;

    /// <summary>One or more checks failed.</summary>
    internal sealed record Red(IReadOnlyList<FailingCheck> FailingChecks) : Checks;

    /// <summary>Checks are in progress or scheduled — no verdict yet.</summary>
    internal sealed record Pending : Checks;

    /// <summary>
    /// Checks exist but are in a terminal non-success/non-failure state
    /// (e.g., canceled or manual-trigger-only).
    /// </summary>
    internal sealed record Unfinished : Checks;

    /// <summary>No checks were detected on this PR.</summary>
    internal sealed record NoneDetected : Checks;

    /// <summary>Provider returned 401/403 — check results could not be read.</summary>
    internal sealed record Unreadable : Checks;

    /// <summary>
    /// Maps a GitHub <c>statusCheckRollup.state</c> value to a <see cref="Checks"/> verdict.
    /// An unrecognized state maps to <see cref="Pending"/> so the gate waits rather than falsely resolving.
    /// </summary>
    internal static Checks FromGitHub(string? rollupState, IReadOnlyList<FailingCheck> failing) =>
        rollupState?.ToUpperInvariant() switch
        {
            null => new NoneDetected(),
            "SUCCESS" => new Green(),
            "FAILURE" or "ERROR" => new Red(failing),
            "PENDING" or "EXPECTED" => new Pending(),
            // Unknown state: wait conservatively rather than falsely resolving.
            _ => new Pending(),
        };

    /// <summary>
    /// Maps a GitLab <c>head_pipeline.status</c> value to a <see cref="Checks"/> verdict.
    /// An unrecognized status maps to <see cref="Pending"/> so the gate waits rather than falsely resolving.
    /// GitLab does not expose per-job failure details at the pipeline level, so <see cref="Red"/> carries an empty list.
    /// </summary>
    internal static Checks FromGitLab(string? pipelineStatus) =>
        pipelineStatus switch
        {
            null => new NoneDetected(),
            "success" or "skipped" => new Green(),
            "failed" => new Red([]),
            "created" or "waiting_for_resource" or "preparing" or "pending"
                or "running" or "scheduled" or "waiting_for_callback" => new Pending(),
            "canceled" or "canceling" or "manual" => new Unfinished(),
            // Unknown status: wait conservatively rather than falsely resolving.
            _ => new Pending(),
        };
}
