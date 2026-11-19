namespace Foundry.Modules.Settings.Domain.ValueObjects;

/// <summary>
/// Dispatch held for every repository that declares Package Sources, because a worker could not reach the Package
/// Source Proxy. The proxy is one endpoint for all repositories, so there is one pause. A background probe re-checks
/// the proxy on the same backoff as a <see cref="HostPause"/> and clears it; Resume All clears it too.
/// </summary>
public sealed record PackageProxyPause
{
    private PackageProxyPause(DateTimeOffset pausedAt, int probeAttempts, DateTimeOffset nextProbeAt)
    {
        PausedAt = pausedAt;
        ProbeAttempts = probeAttempts;
        NextProbeAt = nextProbeAt;
    }

    public DateTimeOffset PausedAt { get; }

    /// <summary>How many probes have found the proxy still unreachable.</summary>
    public int ProbeAttempts { get; }

    public DateTimeOffset NextProbeAt { get; }

    public static PackageProxyPause Start(DateTimeOffset now) =>
        new(now, probeAttempts: 0, now + ProbeBackoff.FirstProbeDelay);

    /// <summary>Rebuilds a stored pause exactly as it was saved.</summary>
    internal static PackageProxyPause Restore(DateTimeOffset pausedAt, int probeAttempts, DateTimeOffset nextProbeAt) =>
        new(pausedAt, Math.Max(0, probeAttempts), nextProbeAt);

    public PackageProxyPause AfterUnreachableProbe(DateTimeOffset now) =>
        new(PausedAt, ProbeAttempts + 1, now + ProbeBackoff.DelayAfterUnreachableProbe(ProbeAttempts));
}
