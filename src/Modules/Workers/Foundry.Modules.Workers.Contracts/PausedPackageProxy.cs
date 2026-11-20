namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// The Package Source Proxy found unreachable: dispatch for repositories that declare Package Sources is held until a
/// probe reaches the proxy again, or Resume All clears it.
/// </summary>
public sealed record PausedPackageProxy(
    DateTimeOffset PausedAt,
    int ProbeAttempts,
    DateTimeOffset NextProbeAt);
