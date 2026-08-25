using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

        return MapToInfo(repo);
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
            .Select(r => new EligibleRepository(r.Id.Value, r.Position))
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

    private static RepositoryEligibilityInfo MapToInfo(MonitoredRepository repo) =>
        RepositoryMappings.ToEligibilityInfo(repo.Eligibility)
        ?? new RepositoryEligibilityInfo(repo.EligibilityStatus!, [], null);
}
