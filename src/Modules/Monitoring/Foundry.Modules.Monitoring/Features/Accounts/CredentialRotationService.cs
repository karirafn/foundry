using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal sealed class CredentialRotationService(
    DbContext dbContext,
    INamespaceDeriver namespaceDeriver,
    IRepositoryEligibilityEvaluator eligibilityEvaluator)
{
    private const int ConcurrencyBound = 4;

    public async Task<IReadOnlyList<AffectedRepository>> RotateAsync(
        Credential credential,
        CancellationToken cancellationToken)
    {
        // Snapshot repos covered by the credential before namespace change
        List<MonitoredRepository> beforeRepos = await FindResolvingReposAsync(credential, cancellationToken);

        // Snapshot their eligibility before re-evaluation
        Dictionary<Guid, string> priorStatus = beforeRepos.ToDictionary(
            r => r.Id.Value,
            r => r.EligibilityStatus ?? "unreachable");

        NamespaceDerivationOutcome outcome = await namespaceDeriver.DeriveAsync(credential, cancellationToken);

        switch (outcome)
        {
            case NamespaceDerivationOutcome.Derived derived:
                credential.SetNamespaces(derived.Namespaces);
                break;
            case NamespaceDerivationOutcome.Unavailable:
                // Keep prior namespaces — do not drop coverage on transient failure
                break;
            default:
                break;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Union of repos before and after the namespace change
        List<MonitoredRepository> afterRepos = await FindResolvingReposAsync(credential, cancellationToken);

        HashSet<Guid> allRepoIds = [..beforeRepos.Select(r => r.Id.Value), ..afterRepos.Select(r => r.Id.Value)];
        Dictionary<Guid, MonitoredRepository> allReposById = beforeRepos
            .Concat(afterRepos)
            .DistinctBy(r => r.Id.Value)
            .ToDictionary(r => r.Id.Value);

        await EvaluateBoundedAsync(allReposById.Values.ToList(), cancellationToken);

        // Reload repos to get updated eligibility status after SaveChanges inside evaluator
        await dbContext.SaveChangesAsync(cancellationToken);

        List<AffectedRepository> affected = [];
        foreach (Guid repoId in allRepoIds)
        {
            if (!allReposById.TryGetValue(repoId, out MonitoredRepository? repo))
            {
                continue;
            }

            string previous = priorStatus.TryGetValue(repoId, out string? prior) ? prior : "unreachable";
            string current = repo.EligibilityStatus ?? "unreachable";

            if (previous != current)
            {
                affected.Add(new AffectedRepository(repoId, repo.Slug.FullPath, previous, current));
            }
        }

        return affected;
    }

    private async Task<List<MonitoredRepository>> FindResolvingReposAsync(
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

    private async Task EvaluateBoundedAsync(
        List<MonitoredRepository> repos,
        CancellationToken cancellationToken)
    {
        using SemaphoreSlim semaphore = new(ConcurrencyBound, ConcurrencyBound);

        async Task EvaluateOneAsync(MonitoredRepository repo)
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await eligibilityEvaluator.EvaluateAndStoreAsync(repo, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }

        await Task.WhenAll(repos.Select(EvaluateOneAsync));
    }
}
