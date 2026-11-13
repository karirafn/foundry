using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;

namespace Foundry.Modules.Workers.Features.Publishing;

/// <summary>
/// Answers, for one publishing tick, whether a run's repository is on a paused provider host (#885). A paused host
/// holds its runs' publishes as the dispatch candidate selector holds its queued issues, so an outage spends none of a
/// run's publish attempts. Each repository's host is read once per tick, and only while some host is paused.
/// </summary>
internal sealed class PublishHostGate
{
    private readonly IReadOnlyList<PausedHost> _pausedHosts;
    private readonly IRepositoryDispatchQueries _repositoryDispatchQueries;
    private readonly Dictionary<MonitoredRepositoryId, bool> _pausedByRepository = [];

    private PublishHostGate(IReadOnlyList<PausedHost> pausedHosts, IRepositoryDispatchQueries repositoryDispatchQueries)
    {
        _pausedHosts = pausedHosts;
        _repositoryDispatchQueries = repositoryDispatchQueries;
    }

    public static async Task<PublishHostGate> ReadAsync(
        IGlobalSettingsQueries settingsQueries,
        IRepositoryDispatchQueries repositoryDispatchQueries,
        CancellationToken cancellationToken)
    {
        DispatchPauseState pauseState = await settingsQueries.GetDispatchPauseStateAsync(cancellationToken);
        return new PublishHostGate(pauseState.PausedHosts, repositoryDispatchQueries);
    }

    /// <summary>
    /// Whether <paramref name="repositoryId"/>'s host is paused. A repository that no longer resolves is not held: its
    /// publish fails on its own, as it does with no pause.
    /// </summary>
    public async Task<bool> IsHeldAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken)
    {
        if (_pausedHosts.Count == 0)
        {
            return false;
        }

        if (_pausedByRepository.TryGetValue(repositoryId, out bool known))
        {
            return known;
        }

        RepositoryDispatchInfo? info = await _repositoryDispatchQueries.GetDispatchInfoAsync(repositoryId, cancellationToken);
        bool paused = info is not null && _pausedHosts.Any(
            pause => string.Equals(pause.Host, info.Host, StringComparison.OrdinalIgnoreCase));
        _pausedByRepository[repositoryId] = paused;
        return paused;
    }
}
