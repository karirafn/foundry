using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class RepositoryDispatchQueries(DbContext db) : IRepositoryDispatchQueries
{
    public async Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        return await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => r.Id == repositoryId)
            .Join(
                db.Set<Account>().AsNoTracking(),
                r => r.AccountId,
                a => a.Id,
                (r, a) => new RepositoryDispatchInfo(
                    r.Slug.ToString(),
                    new Uri(a.BaseUrl, $"{r.Slug}.git"),
                    a.Token))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
