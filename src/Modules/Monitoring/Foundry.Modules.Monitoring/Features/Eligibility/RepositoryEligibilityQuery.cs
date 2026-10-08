using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

internal sealed class RepositoryEligibilityQuery(DbContext db) : IRepositoryEligibilityQuery
{
    private const string EligibleStatus = "eligible";

    public async Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        MonitoredRepositoryId id = MonitoredRepositoryId.From(repositoryId);

        MonitoredRepository? repo = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (repo is null || repo.EligibilityStatus is null)
        {
            return null;
        }

        string providerType = await ResolveProviderTypeAsync(repo.Host, cancellationToken);

        return MapToInfo(repo, providerType);
    }

    public async Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
        IReadOnlyCollection<Guid> repositoryIds,
        CancellationToken cancellationToken)
    {
        if (repositoryIds.Count == 0)
        {
            return [];
        }

        HashSet<MonitoredRepositoryId> typedIds = repositoryIds
            .Select(MonitoredRepositoryId.From)
            .ToHashSet();

        List<EligibleRepository> eligibleRepositories = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => typedIds.Contains(r.Id))
            .Where(r => r.EligibilityStatus == EligibleStatus)
            .Select(r => new EligibleRepository(r.Id.Value, r.Position, r.MaxConcurrentWorkers))
            .ToListAsync(cancellationToken);

        return eligibleRepositories;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
        IReadOnlyCollection<Guid> repositoryIds,
        CancellationToken cancellationToken)
    {
        if (repositoryIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        HashSet<MonitoredRepositoryId> typedIds = repositoryIds
            .Select(MonitoredRepositoryId.From)
            .ToHashSet();

        Dictionary<Guid, string> statuses = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => typedIds.Contains(r.Id))
            .Where(r => r.EligibilityStatus != null)
            .Select(r => new { Id = r.Id.Value, Status = r.EligibilityStatus! })
            .ToDictionaryAsync(r => r.Id, r => r.Status, cancellationToken);

        return statuses;
    }

    private async Task<string> ResolveProviderTypeAsync(string host, CancellationToken cancellationToken)
    {
        // Resolve the credential that owns this host to determine the provider type.
        // One-provider-per-host is a structural invariant: MonitoredRepository.Host is derived from
        // credential.BaseUrl.Value.Host at creation, so a host maps to at most one provider type.
        // When no credential is found (orphan host), fall back to a neutral default so the mapping never throws.
        Credential? credential = await db.Set<Credential>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Host == host, cancellationToken);

        return credential switch
        {
            GitHubCredential => ProviderTypes.GitHub,
            GitLabCredential => ProviderTypes.GitLab,
            // Null (no credential for host) or unknown subtype: return a neutral value so
            // NoPushAccessPreamble's generic `_` arm is hit instead of GitHub-specific wording.
            _ => string.Empty,
        };
    }

    private static RepositoryEligibilityInfo MapToInfo(MonitoredRepository repo, string providerType) =>
        RepositoryMappings.ToEligibilityInfo(repo.Eligibility, providerType)
        ?? new RepositoryEligibilityInfo(repo.EligibilityStatus!, [], null);
}
