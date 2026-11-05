using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.CredentialResolution;

internal sealed class CredentialResolver(DbContext db, ILogger<CredentialResolver> logger) : ICredentialResolver
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

        Credential? best = SelectMostSpecific(candidates, slug);

        if (best is not null && best.IsTokenUnreadable)
        {
            logger.LogWarning(CredentialWarnings.UnreadableToken, best.Id.Value);
        }

        return best;
    }

    /// <summary>
    /// Picks the candidate whose covering namespace for <paramref name="slug"/> is the most specific
    /// (most segments); the first such candidate wins a tie. Candidates must already be narrowed to
    /// the repository's host. Shared by single and batch resolution so both apply one rule.
    /// </summary>
    internal static Credential? SelectMostSpecific(IEnumerable<Credential> candidates, RepositorySlug slug)
    {
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
