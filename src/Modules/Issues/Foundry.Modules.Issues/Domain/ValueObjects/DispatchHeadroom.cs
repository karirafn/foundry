using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Per-repository remaining dispatch slots, derived from eligible repository limits and
/// current in-flight worker counts.
/// </summary>
/// <remarks>
/// Use <see cref="ForClaim"/> in the dispatch selector — it reflects capacity as it is right now.
/// Use <see cref="ForForecast"/> in the dashboard — under global saturation it models the moment
/// the next global slot opens (one in-flight worker per repository is assumed to finish).
/// </remarks>
internal sealed class DispatchHeadroom
{
    private readonly IReadOnlyDictionary<MonitoredRepositoryId, int> _byRepo;

    private DispatchHeadroom(IReadOnlyDictionary<MonitoredRepositoryId, int> byRepo) =>
        _byRepo = byRepo;

    /// <summary>
    /// Remaining slots for <paramref name="repositoryId"/>; 0 when absent (matches
    /// <c>InFlightWorkerCountQuery</c> absent-means-zero semantics).
    /// </summary>
    public int For(MonitoredRepositoryId repositoryId) =>
        _byRepo.TryGetValue(repositoryId, out int slots) ? slots : 0;

    /// <summary>
    /// Returns a mutable working copy of the backing dictionary. The walk decrements this
    /// copy — the value object's own dictionary is never mutated.
    /// </summary>
    internal IReadOnlyDictionary<MonitoredRepositoryId, int> Snapshot => _byRepo;

    /// <summary>
    /// Builds claim headroom: <c>max(0, MaxConcurrentWorkers − inFlight)</c> per repository.
    /// Repositories absent from <paramref name="inFlightByRepo"/> are treated as having 0
    /// in-flight workers (absent-means-zero, matching <c>InFlightWorkerCountQuery</c>).
    /// </summary>
    public static DispatchHeadroom ForClaim(
        IReadOnlyList<EligibleRepository> repositories,
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlightByRepo)
    {
        Dictionary<MonitoredRepositoryId, int> byRepo = new(repositories.Count);

        foreach (EligibleRepository repo in repositories)
        {
            MonitoredRepositoryId id = MonitoredRepositoryId.From(repo.Id);
            inFlightByRepo.TryGetValue(id, out int inFlight);
            byRepo[id] = Math.Max(0, repo.MaxConcurrentWorkers - inFlight);
        }

        return new DispatchHeadroom(byRepo);
    }

    /// <summary>
    /// Builds forecast headroom for the dashboard. When global slots are saturated
    /// (<c>sum(inFlight) &gt;= globalMax</c>), each repository with in-flight workers
    /// gets <c>max(0, MaxConcurrentWorkers − (inFlight − 1))</c> — modelling the one
    /// running worker whose completion opens the next global slot. Idle repositories
    /// keep their full <c>MaxConcurrentWorkers</c>. Below saturation, returns the same
    /// result as <see cref="ForClaim"/>.
    /// </summary>
    public static DispatchHeadroom ForForecast(
        IReadOnlyList<EligibleRepository> repositories,
        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlightByRepo,
        int globalMax)
    {
        int total = inFlightByRepo.Values.Sum();

        if (total < globalMax)
        {
            return ForClaim(repositories, inFlightByRepo);
        }

        Dictionary<MonitoredRepositoryId, int> byRepo = new(repositories.Count);

        foreach (EligibleRepository repo in repositories)
        {
            MonitoredRepositoryId id = MonitoredRepositoryId.From(repo.Id);
            inFlightByRepo.TryGetValue(id, out int inFlight);

            int slots = inFlight > 0
                ? Math.Max(0, repo.MaxConcurrentWorkers - (inFlight - 1))
                : repo.MaxConcurrentWorkers;

            byRepo[id] = slots;
        }

        return new DispatchHeadroom(byRepo);
    }
}
