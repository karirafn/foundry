using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// Builds the provider for a monitored repository with the account PAT that covers it, resolved at call time.
/// Failures carry <see cref="PostExitProviderQueriesErrors"/> codes.
/// </summary>
internal static class RepositoryProviderResolution
{
    public static async Task<Result<(IIssueProvider Provider, MonitoredRepository Repo)>> ResolveAsync(
        DbContext dbContext,
        ICredentialResolver credentialResolver,
        IIssueProviderFactory providerFactory,
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        MonitoredRepository? repo = await dbContext.Set<MonitoredRepository>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

        if (repo is null)
        {
            return Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.RepositoryNotFound(repositoryId));
        }

        Credential? credential = await credentialResolver.ResolveAsync(
            repo.Host,
            repo.Slug,
            cancellationToken);

        if (credential is null)
        {
            return Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.CredentialNotFound(repositoryId));
        }

        TokenUsability usability = credential.TokenUsability;
        return usability switch
        {
            TokenUsability.Usable usable => Result<(IIssueProvider, MonitoredRepository)>.Ok(
                (providerFactory.CreateProvider(credential, usable.Token), repo)),
            TokenUsability.Unreadable => Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.CredentialTokenUnreadable(credential.Id)),
            TokenUsability.Absent => Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.CredentialTokenNotConfigured(credential.Id)),
            _ => throw new UnreachableException($"Unhandled TokenUsability variant: {usability.GetType().Name}"),
        };
    }
}
