using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.CredentialResolution;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal sealed class RepositorySlugQueries(DbContext db) : IRepositorySlugQueries
{
    public async Task<IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo>> GetRepositoryDisplayInfoAsync(
        IReadOnlySet<MonitoredRepositoryId> repositoryIds,
        CancellationToken cancellationToken)
    {
        if (repositoryIds.Count == 0)
        {
            return new Dictionary<MonitoredRepositoryId, RepositoryDisplayInfo>();
        }

        List<MonitoredRepository> repositories = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => repositoryIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (repositories.Count == 0)
        {
            return new Dictionary<MonitoredRepositoryId, RepositoryDisplayInfo>();
        }

        List<string> hosts = repositories
            .Select(r => r.Host)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<Credential> credentials = await db.Set<Credential>()
            .AsNoTracking()
            .Where(c => hosts.Contains(c.Host))
            .Include(c => c.Namespaces)
            .ToListAsync(cancellationToken);

        ILookup<string, Credential> credentialsByHost = credentials.ToLookup(c => c.Host, StringComparer.Ordinal);

        return repositories.ToDictionary(
            r => r.Id,
            r => new RepositoryDisplayInfo(
                r.Slug.ToString(),
                ToProviderType(CredentialResolver.SelectMostSpecific(credentialsByHost[r.Host], r.Slug))));
    }

    private static string? ToProviderType(Credential? credential) =>
        credential switch
        {
            GitHubCredential => ProviderTypes.GitHub,
            GitLabCredential => ProviderTypes.GitLab,
            _ => null,
        };
}
