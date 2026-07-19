using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class CredentialResolver(DbContext db) : ICredentialResolver
{
    public async Task<Credential?> ResolveAsync(
        string host,
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        List<Credential> candidates = await db.Set<Credential>()
            .AsNoTracking()
            .Where(c => c.Host == host)
            .Include(c => c.Namespaces)
            .ToListAsync(cancellationToken);

        Credential? best = null;
        int bestSegmentCount = -1;

        foreach (Credential candidate in candidates)
        {
            Namespace? covering = candidate.ResolveCoveringNamespace(slug);

            if (covering is null)
            {
                continue;
            }

            if (covering.SegmentCount > bestSegmentCount)
            {
                bestSegmentCount = covering.SegmentCount;
                best = candidate;
            }
        }

        return best;
    }
}
