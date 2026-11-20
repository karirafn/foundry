namespace Foundry.Modules.Workers.Contracts.Queries;

/// <summary>
/// Every dispatch-pause reason. <see cref="IsDispatchPaused"/> and <see cref="UsageLimitResetsAt"/> hold all dispatch;
/// each of <see cref="PausedHosts"/> holds only the repositories on its host, and <see cref="PackageProxyPause"/> holds only
/// the repositories that declare Package Sources.
/// </summary>
public sealed record DispatchPauseState(
    DateTimeOffset? UsageLimitResetsAt,
    bool IsDispatchPaused,
    IReadOnlyList<PausedHost> PausedHosts,
    PausedPackageProxy? PackageProxyPause = null);

public interface IDispatchPauseQueries
{
    Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken);
}
