using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

internal sealed class HostRepositoryQueries(DbContext db) : IHostRepositoryQueries
{
    public async Task<IReadOnlyList<MonitoredRepositoryId>> GetRepositoryIdsOnHostAsync(
        string host,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        // MonitoredRepository.Host is the credential URL's Uri.Host, which is always lower-case.
        string normalized = host.Trim().ToLowerInvariant();

        return await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => r.Host == normalized)
            .OrderBy(r => r.Position)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
    }
}
