using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities;

/// <summary>
/// Every runtime reason dispatch is held, on one row: the operator's manual pause, the usage-limit pause, the per-host
/// pauses and the package-proxy pause. Keeping them together lets Resume All clear them in one method, and lets a
/// pause be staged unsaved so it commits in the same save as the run failure that found its cause.
/// </summary>
public sealed class DispatchPause : AggregateRoot<DispatchPauseId>
{
    internal const int MaxUsageLimitResetDays = 7;

    private DispatchPause() : base(DispatchPauseId.Default)
    {
    }

    private DispatchPause(DispatchPauseId id, DateTimeOffset createdAt) : base(id)
    {
        UpdatedAt = createdAt;
    }

    public DateTimeOffset? UsageLimitResetsAt { get; private set; }

    public bool IsDispatchPaused { get; private set; }

    /// <summary>
    /// The provider hosts dispatch is held for because a Publisher found them unreachable, one pause per host. A pause
    /// reason beside <see cref="IsDispatchPaused"/> and <see cref="UsageLimitResetsAt"/>, but it holds only its host.
    /// </summary>
    public IReadOnlyList<HostPause> HostPauses { get; private set; } = [];

    /// <summary>
    /// Dispatch held for every repository that declares Package Sources because a worker could not reach the Package
    /// Source Proxy; <see langword="null"/> when the proxy is not paused.
    /// </summary>
    public PackageProxyPause? PackageProxyPause { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static DispatchPause Create() => new(DispatchPauseId.Default, DateTimeOffset.UtcNow);

    public void PauseDispatch()
    {
        IsDispatchPaused = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Clears every dispatch-pause reason — the operator's manual pause, the usage-limit
    /// pause, every host pause and the package-proxy pause — for the manual "Resume All" action.
    /// </summary>
    public void ResumeAllDispatch()
    {
        IsDispatchPaused = false;
        UsageLimitResetsAt = null;
        HostPauses = [];
        PackageProxyPause = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Holds dispatch to <paramref name="host"/> from <paramref name="now"/>. A host already paused keeps its pause
    /// as it is, so concurrent failures on one host never reset its probe backoff. Returns <c>true</c> when a pause
    /// was added.
    /// </summary>
    public bool PauseHost(string host, DateTimeOffset now)
    {
        if (FindHostPause(host) is not null)
        {
            return false;
        }

        HostPauses = [.. HostPauses, HostPause.Start(host, now)];
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Records that a probe at <paramref name="now"/> found <paramref name="host"/> still unreachable, pushing its
    /// next probe out on the backoff. Returns <c>false</c> when the host is not paused (Resume All cleared it).
    /// </summary>
    public bool RecordProbeUnreachable(string host, DateTimeOffset now)
    {
        if (FindHostPause(host) is not { } pause)
        {
            return false;
        }

        HostPause advanced = pause.AfterUnreachableProbe(now);
        HostPauses = [.. HostPauses.Select(p => ReferenceEquals(p, pause) ? advanced : p)];
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Lifts the pause on <paramref name="host"/>. Returns <c>false</c> when the host was not paused.</summary>
    public bool ClearHostPause(string host)
    {
        if (FindHostPause(host) is not { } pause)
        {
            return false;
        }

        HostPauses = [.. HostPauses.Where(p => !ReferenceEquals(p, pause))];
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Holds dispatch for repositories with Package Sources from <paramref name="now"/>. A pause already present is
    /// left as it is, so concurrent failures never reset its probe backoff. Returns <c>true</c> when a pause was added.
    /// </summary>
    public bool PausePackageProxy(DateTimeOffset now)
    {
        if (PackageProxyPause is not null)
        {
            return false;
        }

        PackageProxyPause = PackageProxyPause.Start(now);
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Records that a probe at <paramref name="now"/> found the proxy still unreachable, pushing the next probe out on
    /// the backoff. Returns <c>false</c> when the proxy is not paused (Resume All cleared it).
    /// </summary>
    public bool RecordPackageProxyProbeUnreachable(DateTimeOffset now)
    {
        if (PackageProxyPause is not { } pause)
        {
            return false;
        }

        PackageProxyPause = pause.AfterUnreachableProbe(now);
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Lifts the package-proxy pause. Returns <c>false</c> when the proxy was not paused.</summary>
    public bool ClearPackageProxyPause()
    {
        if (PackageProxyPause is null)
        {
            return false;
        }

        PackageProxyPause = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Clears the usage-limit pause reason only. The operator's manual dispatch pause
    /// (<see cref="IsDispatchPaused"/>) is left untouched, so a manual pause survives a
    /// usage-limit auto-resume. Returns <c>true</c> when dispatch is now fully unpaused
    /// (no manual pause remains), <c>false</c> when a manual pause still holds dispatch.
    /// </summary>
    public bool ClearUsageLimitPause()
    {
        UsageLimitResetsAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        return !IsDispatchPaused;
    }

    public void SetUsageLimitResetsAt(DateTimeOffset resetsAt)
    {
        if (resetsAt <= DateTimeOffset.UtcNow)
        {
            return;
        }

        DateTimeOffset maxResetTime = DateTimeOffset.UtcNow.AddDays(MaxUsageLimitResetDays);
        DateTimeOffset clampedResetsAt = resetsAt > maxResetTime ? maxResetTime : resetsAt;

        UsageLimitResetsAt = UsageLimitResetsAt.HasValue && UsageLimitResetsAt.Value > clampedResetsAt
            ? UsageLimitResetsAt
            : clampedResetsAt;

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private HostPause? FindHostPause(string host) => HostPauses.FirstOrDefault(p => p.IsFor(host));
}
