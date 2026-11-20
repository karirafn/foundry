namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The probe schedule every unreachable-endpoint pause shares: the first probe a minute after the pause, then
/// 2^(attempts so far + 1) minutes after each probe that still fails, at most 10, so probes run 1, 2, 4 and 8 minutes
/// apart, then every 10, without limit.
/// </summary>
internal static class ProbeBackoff
{
    public static readonly TimeSpan FirstProbeDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan MaxProbeDelay = TimeSpan.FromMinutes(10);

    // Past this many failed probes the doubled delay already exceeds MaxProbeDelay; capping the exponent keeps the
    // arithmetic from overflowing on a long outage.
    private const int MaxDoublings = 4;

    public static TimeSpan DelayAfterUnreachableProbe(int probeAttemptsSoFar)
    {
        int doublings = Math.Min(probeAttemptsSoFar + 1, MaxDoublings);
        TimeSpan doubled = FirstProbeDelay * (1 << doublings);
        return doubled < MaxProbeDelay ? doubled : MaxProbeDelay;
    }
}
