namespace Foundry.Modules.Settings.Contracts;

/// <summary>A provider host whose dispatch is held until a probe reaches it again, or Resume All clears it.</summary>
public sealed record PausedHost(
    string Host,
    DateTimeOffset PausedAt,
    int ProbeAttempts,
    DateTimeOffset NextProbeAt);
