namespace Foundry.Modules.Credentials.Features.CreditProbe;

/// <summary>
/// Discriminated result returned by <see cref="CreditProbeCoordinator.TryRunProbeAsync"/>,
/// capturing what happened so callers (e.g. the probe endpoint) can produce a meaningful response.
/// </summary>
public abstract record CreditProbeResult
{
    private CreditProbeResult() { }

    /// <summary>The probe ran and credits are available; any credit block was cleared.</summary>
    public sealed record Restored : CreditProbeResult;

    /// <summary>The probe ran; credits are still exhausted. The probe is re-armed.</summary>
    public sealed record StillBlocked(DateTimeOffset NextProbeAt) : CreditProbeResult;

    /// <summary>
    /// The probe detected a rate-based usage limit. The global usage-limit pause was set and the
    /// credit block was cleared — the operative constraint is now the usage-limit timer.
    /// </summary>
    public sealed record UsageLimited(DateTimeOffset ResetsAt) : CreditProbeResult;

    /// <summary>
    /// The probe container failed to run or returned an unclassifiable result.
    /// The probe is re-armed; no credit state was changed.
    /// </summary>
    public sealed record InfrastructureFailure(DateTimeOffset NextProbeAt) : CreditProbeResult;

    /// <summary>A login session is in progress; the probe was deferred and the probe arm was refreshed.</summary>
    public sealed record Deferred : CreditProbeResult;

    /// <summary>Another probe is already running; this call returned immediately without probing.</summary>
    public sealed record AlreadyRunning : CreditProbeResult;

    /// <summary>No <c>ClaudeAccount</c> row exists; nothing to probe.</summary>
    public sealed record NoAccount : CreditProbeResult;

    /// <summary>The account spend state is not <c>Blocked</c>; probing is a no-op.</summary>
    public sealed record NotBlocked : CreditProbeResult;
}
