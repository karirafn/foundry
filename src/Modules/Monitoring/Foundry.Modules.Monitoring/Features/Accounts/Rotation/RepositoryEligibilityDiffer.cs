using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Eligibility;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts.Rotation;

/// <summary>
/// Snapshots prior eligibility for a set of repositories, re-evaluates them, and returns
/// the subset whose eligibility status changed.
/// </summary>
/// <remarks>
/// Evaluation is sequential — DbContext is not thread-safe; concurrent calls to
/// EvaluateAndStoreAsync would access the same DbContext instance from multiple threads.
/// </remarks>
internal sealed class RepositoryEligibilityDiffer(
    DbContext dbContext,
    IRepositoryEligibilityEvaluator eligibilityEvaluator)
{
    /// <summary>
    /// Re-evaluates all repositories in the before∪after union, persists eligibility changes,
    /// and returns those whose status differed from the snapshot.
    /// The <paramref name="priorStatus"/> snapshot must be taken before calling this method.
    /// </summary>
    public async Task<IReadOnlyList<AffectedRepository>> DiffAsync(
        IReadOnlyList<MonitoredRepository> reposBefore,
        IReadOnlyList<MonitoredRepository> reposAfter,
        Dictionary<Guid, string> priorStatus,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, MonitoredRepository> allReposById = reposBefore
            .Concat(reposAfter)
            .DistinctBy(r => r.Id.Value)
            .ToDictionary(r => r.Id.Value);

        foreach (MonitoredRepository repo in allReposById.Values)
        {
            await eligibilityEvaluator.EvaluateAndStoreAsync(repo, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        List<AffectedRepository> affected = [];
        foreach (MonitoredRepository repo in allReposById.Values)
        {
            string previous = priorStatus.TryGetValue(repo.Id.Value, out string? prior) ? prior : "unreachable";
            string current = repo.EligibilityStatus ?? "unreachable";

            if (previous != current)
            {
                affected.Add(new AffectedRepository(repo.Id.Value, repo.Slug.FullPath, previous, current));
            }
        }

        return affected;
    }

    /// <summary>
    /// Finds tracked repositories covered by <paramref name="credential"/> on the same host.
    /// Entities are tracked on purpose — EvaluateAndStoreAsync calls SetEligibility on them,
    /// and the outer SaveChangesAsync persists those mutations.
    /// </summary>
    public async Task<List<MonitoredRepository>> FindResolvingReposAsync(
        Credential credential,
        CancellationToken cancellationToken)
    {
        List<MonitoredRepository> allRepos = await dbContext.Set<MonitoredRepository>()
            .Where(r => r.Host == credential.Host)
            .ToListAsync(cancellationToken);

        return allRepos
            .Where(r => credential.ResolveCoveringNamespace(r.Slug) is not null)
            .ToList();
    }
}
