namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// Dispatch to one provider host held because a run's Publisher found the host unreachable. A background probe
/// re-checks the host on a backoff and clears the pause; Resume All clears it too.
/// </summary>
public sealed record HostPause
{
    private HostPause(string host, DateTimeOffset pausedAt, int probeAttempts, DateTimeOffset nextProbeAt)
    {
        Host = host;
        PausedAt = pausedAt;
        ProbeAttempts = probeAttempts;
        NextProbeAt = nextProbeAt;
    }

    /// <summary>The paused provider host, trimmed and lower-case (for example <c>gitlab.com</c>).</summary>
    public string Host { get; }

    public DateTimeOffset PausedAt { get; }

    /// <summary>How many probes have found the host still unreachable.</summary>
    public int ProbeAttempts { get; }

    public DateTimeOffset NextProbeAt { get; }

    /// <summary>A new pause on <paramref name="host"/> at <paramref name="now"/>; its first probe is a minute later.</summary>
    public static HostPause Start(string host, DateTimeOffset now) =>
        new(Normalize(host), now, probeAttempts: 0, now + ProbeBackoff.FirstProbeDelay);

    /// <summary>Rebuilds a stored pause exactly as it was saved.</summary>
    internal static HostPause Restore(string host, DateTimeOffset pausedAt, int probeAttempts, DateTimeOffset nextProbeAt) =>
        new(Normalize(host), pausedAt, Math.Max(0, probeAttempts), nextProbeAt);

    /// <summary>
    /// The pause after a probe at <paramref name="now"/> found the host still unreachable: the next probe waits
    /// 2^(attempts so far + 1) minutes, at most 10, so probes run 1, 2, 4 and 8 minutes apart, then every 10.
    /// </summary>
    public HostPause AfterUnreachableProbe(DateTimeOffset now)
    {
        return new HostPause(Host, PausedAt, ProbeAttempts + 1, now + ProbeBackoff.DelayAfterUnreachableProbe(ProbeAttempts));
    }

    public bool IsProbeDue(DateTimeOffset now) => NextProbeAt <= now;

    /// <summary>Whether this pause is for <paramref name="host"/>, compared as hosts are: case-insensitively.</summary>
    public bool IsFor(string host) => string.Equals(Host, host?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return host.Trim().ToLowerInvariant();
    }
}
