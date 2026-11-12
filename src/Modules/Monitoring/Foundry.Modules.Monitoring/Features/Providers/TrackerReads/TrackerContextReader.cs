using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Providers.TrackerReads;

internal sealed class TrackerContextReader(
    DbContext dbContext,
    IIssueProviderFactory providerFactory,
    ICredentialResolver credentialResolver,
    ILogger<TrackerContextReader> logger)
    : ITrackerContextQueries
{
    private const int MaxOpenIssues = 100;

    public async Task<TrackerContext> GetTrackerContextAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        IssueContent issueContent,
        CancellationToken cancellationToken)
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await RepositoryProviderResolution.ResolveAsync(
                dbContext, credentialResolver, providerFactory, repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            logger.LogWarning(
                "Resolving the provider for the tracker context of issue #{IssueNumber} failed: {Error}",
                issueNumber,
                ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error.Message);
            return new TrackerContext([], [], false, null);
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        IReadOnlyList<OpenIssue>? openIssues =
            await ReadOpenIssuesAsync(provider, repo.Slug, issueNumber, cancellationToken);

        return new TrackerContext([], [], false, openIssues);
    }

    private async Task<IReadOnlyList<OpenIssue>?> ReadOpenIssuesAsync(
        IIssueProvider provider,
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<OpenIssue>> result;
        try
        {
            result = await provider.GetOpenIssuesAsync(slug, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Reading the open issues for the tracker context of issue #{IssueNumber} threw.", issueNumber);
            return null;
        }

        if (result is not Result<IReadOnlyList<OpenIssue>>.Success success)
        {
            logger.LogWarning(
                "Reading the open issues for the tracker context of issue #{IssueNumber} failed: {Error}",
                issueNumber,
                ((Result<IReadOnlyList<OpenIssue>>.Failure)result).Error.Message);
            return null;
        }

        return
        [
            .. success.Value
                .Where(openIssue => openIssue.Number != issueNumber)
                .Take(MaxOpenIssues),
        ];
    }
}
