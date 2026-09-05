using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.CredentialResolution;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

internal sealed class RepositoryDispatchQueries(
    DbContext db,
    ICredentialResolver credentialResolver) : IRepositoryDispatchQueries
{
    public async Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
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

        if (credential is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(credential.Token))
        {
            return null;
        }

        WorkerProvider provider = credential switch
        {
            GitHubCredential => new WorkerProvider.GitHub(),
            GitLabCredential => new WorkerProvider.GitLab(),
            _ => throw new UnreachableException($"Unknown credential type: {credential.GetType().Name}"),
        };

        string issueApiUrlBase = IssueApiUrlBuilder.BuildBase(credential, repo.Slug);

        return new RepositoryDispatchInfo(
            repo.Slug.ToString(),
            new Uri(credential.BaseUrl.Value, $"{repo.Slug}.git"),
            credential.Token,
            provider,
            issueApiUrlBase);
    }
}
