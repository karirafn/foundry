using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal sealed class RepositoryLifecycleSettingsQueries(DbContext db) : IRepositoryLifecycleSettingsQueries
{
    public async Task<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>> GetLifecycleSettingsAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        if (repositoryIds.Count == 0)
        {
            return new Dictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>();
        }

        return await db.Set<MonitoredRepository>()
            .AsNoTracking()
            // History keeps showing a removed repository's completed issues, with the path its settings give.
            .IgnoreQueryFilters([MonitoredRepositoryConfiguration.RemovedRepositoryFilter])
            .Where(r => repositoryIds.Contains(r.Id))
            .ToDictionaryAsync(
                r => r.Id,
                r => new RepositoryLifecycleSettings(r.RebaseAfterRun, r.ReviewPullRequests, r.MergeWhenChecksPass),
                cancellationToken);
    }
}
