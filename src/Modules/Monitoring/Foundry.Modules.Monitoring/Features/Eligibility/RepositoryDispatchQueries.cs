using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.PackageSourceProxy;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

internal sealed class RepositoryDispatchQueries(
    DbContext db,
    ICredentialResolver credentialResolver,
    IOptions<PackageSourceProxyOptions> proxyOptions,
    ILogger<RepositoryDispatchQueries> logger) : IRepositoryDispatchQueries
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

        TokenUsability usability = credential.TokenUsability;
        string? token = usability switch
        {
            TokenUsability.Usable usable => usable.Token,
            TokenUsability.Unreadable => null,
            TokenUsability.Invalid => null,
            TokenUsability.Absent => null,
            _ => throw new UnreachableException($"Unhandled TokenUsability variant: {usability.GetType().Name}"),
        };

        if (token is null)
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
            token,
            provider,
            issueApiUrlBase,
            repo.Host);
    }

    public async Task<WorkerNuGetConfig?> GetWorkerNuGetConfigAsync(
        MonitoredRepositoryId repositoryId,
        string runSourceToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(runSourceToken);

        MonitoredRepository? repo = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

        if (repo?.RootNuGetConfig is not RootNuGetConfig.Declared { Sources.Count: > 0 } declared)
        {
            return null;
        }

        // The probe stores each declared source; one it has not stored yet has no id for the proxy to serve it by.
        Dictionary<string, PackageSourceId> storedIds = (await db.Set<PackageSource>()
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToDictionary(s => s.ServiceIndexUrl.AbsoluteUri, s => s.Id, StringComparer.Ordinal);

        List<WorkerNuGetConfigBuilder.ProxiedSource> proxied = [];
        foreach (DeclaredPackageSource source in declared.Sources)
        {
            if (storedIds.TryGetValue(source.ServiceIndexUrl.AbsoluteUri, out PackageSourceId sourceId))
            {
                proxied.Add(new WorkerNuGetConfigBuilder.ProxiedSource(source, sourceId));
            }
        }

        if (proxied.Count == 0)
        {
            return null;
        }

        Result<string> built = WorkerNuGetConfigBuilder.Build(
            declared.RawText,
            proxied,
            proxyOptions.Value.WorkerBaseUrl,
            runSourceToken);

        if (built is Result<string>.Failure failure)
        {
            // The stored text parsed when the poll read it, so this is not expected; the worker restores as before.
            logger.LogWarning(
                "Could not build the worker nuget.config of repository {Slug}; its run restores with its own config: {Error}",
                repo.Slug,
                failure.Error.Message);
            return null;
        }

        return new WorkerNuGetConfig(((Result<string>.Success)built).Value);
    }
}
