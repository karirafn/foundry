using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class RepositorySlugQueries(DbContext db) : IRepositorySlugQueries
{
    private const string GitHubDiscriminator = "github";
    private const string GitHubProviderType = "GitHub";
    private const string GitLabProviderType = "GitLab";

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
        string? discriminator = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => r.Id == repositoryId)
            .Join(
                db.Set<Account>().AsNoTracking(),
                r => r.AccountId,
                a => a.Id,
                (r, a) => EF.Property<string>(a, "type"))
            .FirstOrDefaultAsync(cancellationToken);

        return discriminator is null
            ? null
            : discriminator == GitHubDiscriminator ? GitHubProviderType : GitLabProviderType;
    }
}
