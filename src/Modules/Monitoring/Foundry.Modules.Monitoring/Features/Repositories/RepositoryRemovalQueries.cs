using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal sealed class RepositoryRemovalQueries(DbContext db) : IRepositoryRemovalQueries
{
    public Task<bool> IsRemovedAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        db.Set<MonitoredRepository>()
            .AsNoTracking()
            .IgnoreQueryFilters([MonitoredRepositoryConfiguration.RemovedRepositoryFilter])
            .AnyAsync(r => r.Id == repositoryId && r.RemovedAt != null, cancellationToken);
}
