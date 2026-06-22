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
        var rows = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => r.Id == repositoryId)
            .Join(
                db.Set<Account>().AsNoTracking(),
                r => r.AccountId,
                a => a.Id,
                (r, a) => new
                {
                    Slug = r.Slug,
                    BaseUrl = a.BaseUrl,
                    Token = a.Token,
                    ProviderType = EF.Property<string>(a, "type"),
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new RepositoryDispatchInfo(
                x.Slug.ToString(),
                new Uri(x.BaseUrl.Value, $"{x.Slug}.git"),
                x.Token,
                x.ProviderType))
            .FirstOrDefault();
    }
}
