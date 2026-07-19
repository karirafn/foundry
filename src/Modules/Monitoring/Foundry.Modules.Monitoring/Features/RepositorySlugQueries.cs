using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class RepositorySlugQueries(
    DbContext db,
    ICredentialResolver credentialResolver) : IRepositorySlugQueries
{
    public async Task<IReadOnlyDictionary<MonitoredRepositoryId, string>> GetSlugsAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        if (repositoryIds.Count == 0)
        {
            return new Dictionary<MonitoredRepositoryId, string>();
        }

        return await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => repositoryIds.Contains(r.Id))
            .Select(r => new { r.Id, Slug = r.Slug.ToString() })
            .ToDictionaryAsync(r => r.Id, r => r.Slug, cancellationToken);
    }

    public async Task<string?> GetProviderTypeAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        MonitoredRepository? repo = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

        if (repo is null)
        {
            return null;
        }

        Credential? credential = await credentialResolver.ResolveAsync(
            repo.Host,
            repo.Slug,
            cancellationToken);

        return credential switch
        {
            GitHubCredential => "github",
            GitLabCredential => "gitlab",
            null => null,
            _ => null,
        };
    }
}
