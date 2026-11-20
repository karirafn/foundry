using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.CredentialResolution;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// Finds the Unclaimed Repositories: membership is resolved live from the Namespace Claims, never from stored
/// eligibility, so a repository stops being unclaimed as soon as an account claims its namespace.
/// </summary>
internal sealed class UnclaimedRepositories(DbContext dbContext, ICredentialResolver credentialResolver)
{
    public Task<IReadOnlyList<MonitoredRepository>> FindAsync(CancellationToken cancellationToken) =>
        FindAsync(dbContext.Set<MonitoredRepository>(), cancellationToken);

    public Task<IReadOnlyList<MonitoredRepository>> FindOnHostAsync(string host, CancellationToken cancellationToken) =>
        FindAsync(dbContext.Set<MonitoredRepository>().Where(r => r.Host == host), cancellationToken);

    private async Task<IReadOnlyList<MonitoredRepository>> FindAsync(
        IQueryable<MonitoredRepository> candidates,
        CancellationToken cancellationToken)
    {
        List<MonitoredRepository> repositories = await candidates
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        List<MonitoredRepository> unclaimed = [];

        foreach (MonitoredRepository repository in repositories)
        {
            if (await credentialResolver.ResolveAsync(repository.Host, repository.Slug, cancellationToken) is null)
            {
                unclaimed.Add(repository);
            }
        }

        return unclaimed;
    }
}
