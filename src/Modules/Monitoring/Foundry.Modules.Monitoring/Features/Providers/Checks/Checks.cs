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
    /// Whether a GitHub <c>CheckRun.conclusion</c> (<c>CheckConclusionState</c>) names a failed check run.
    /// </summary>
    internal static bool IsGitHubCheckRunFailure(string? conclusion) =>
        conclusion?.ToUpperInvariant() switch
        {
            "FAILURE" or "TIMED_OUT" or "CANCELLED" or "ACTION_REQUIRED" or "STARTUP_FAILURE" => true,
            _ => false,
        };

    /// <summary>
    /// Whether a GitHub <c>StatusContext.state</c> (<c>StatusState</c>) names a failed status context.
    /// </summary>
    internal static bool IsGitHubStatusContextFailure(string? state) =>
        state?.ToUpperInvariant() switch
        {
            "FAILURE" or "ERROR" => true,
            _ => false,
        };

    /// <summary>
    /// Whether a failed GitHub check run's <c>conclusion</c> means it failed before its script ran, so a fix run has
    /// nothing to fix. <c>FAILURE</c> and <c>TIMED_OUT</c> ran the script; billing-locked jobs also report
    /// <c>FAILURE</c> (the reason is only in an annotation), so they stay fixable.
    /// </summary>
    internal static bool IsGitHubCheckRunInfrastructureFailure(string? conclusion) =>
        conclusion?.ToUpperInvariant() switch
        {
            "STARTUP_FAILURE" or "CANCELLED" or "ACTION_REQUIRED" => true,
            _ => false,
        };

    /// <summary>
    /// Whether a failed GitHub status context's <c>state</c> means the external CI errored rather than reported a
    /// failure, so a fix run has nothing to fix.
    /// </summary>
    internal static bool IsGitHubStatusContextInfrastructureFailure(string? state) =>
        string.Equals(state, "ERROR", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a failed GitLab job's <c>failure_reason</c> means it failed before or outside its script, so a fix run
    /// has nothing to fix. The fixable reasons are an allowlist: GitLab keeps adding reasons, and an unknown one
    /// costs a person a look in review, while a wrong fixable verdict spends worker runs. A job with no reason in the
    /// response stays fixable.
    /// </summary>
    internal static bool IsGitLabInfrastructureFailure(string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return false;
        }

        return failureReason switch
        {
            "script_failure" or "job_execution_timeout" or "job_execution_server_timeout"
                or "server_timeout_running" => false,
            _ => true,
        };
    }

    /// <summary>
    /// Maps a GitLab <c>head_pipeline.status</c> value to a <see cref="Checks"/> verdict.
    /// An unrecognized status maps to <see cref="Pending"/> so the gate waits rather than falsely resolving.
    /// </summary>
    internal static Checks FromGitLab(string? pipelineStatus, IReadOnlyList<FailingCheck> failing) =>
        pipelineStatus switch
        {
            null => new NoneDetected(),
            "success" or "skipped" => new Green(),
            "failed" => new Red(failing),
            "created" or "waiting_for_resource" or "preparing" or "pending"
                or "running" or "scheduled" or "waiting_for_callback" => new Pending(),
            "canceled" or "canceling" or "manual" => new Unfinished(),
            // Unknown status: wait conservatively rather than falsely resolving.
            _ => new Pending(),
        };
}
