using System.Text.RegularExpressions;

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
        try
        {
            return await ReadTrackerContextAsync(repositoryId, issueNumber, issueContent, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Reading the tracker context of issue #{IssueNumber} threw.", issueNumber);
            return UnreadableContext(issueNumber, issueContent);
        }
    }

    private async Task<TrackerContext> ReadTrackerContextAsync(
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
            return UnreadableContext(issueNumber, issueContent);
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        Result<IReadOnlyList<IssueReference>> linksRead = await ReadAsync(
            () => provider.GetIssueLinksAsync(repo.Slug, issueNumber, cancellationToken),
            cancellationToken);
        IReadOnlyList<IssueReference> links = linksRead is Result<IReadOnlyList<IssueReference>>.Success linked
            ? linked.Value
            : [];
        List<ReferencedIssue> referencedIssues = [];
        List<int> unreadableIssueNumbers = [];
        foreach (IssueReference reference in ReferencedIssueSelection.Select(issueNumber, issueContent, links))
        {
            Result<ProviderReferencedIssue> read = await ReadAsync(
                () => provider.GetReferencedIssueAsync(repo.Slug, reference.Number, cancellationToken),
                cancellationToken);
            if (read is Result<ProviderReferencedIssue>.Success { Value: { } issue })
            {
                referencedIssues.Add(new ReferencedIssue(
                    reference.Number,
                    reference.Relation,
                    issue.Title,
                    issue.IsOpen,
                    issue.Labels,
                    issue.ClosingMergeRequest,
                    issue.Body));
            }
            else if (read is not Result<ProviderReferencedIssue>.Failure { Error.Code: TrackerReadErrors.NotAnIssueCode })
            {
                unreadableIssueNumbers.Add(reference.Number);
            }
        }

        IReadOnlyList<OpenIssue>? openIssues =
            await ReadOpenIssuesAsync(provider, repo.Slug, issueNumber, cancellationToken);

        return new TrackerContext(
            referencedIssues,
            unreadableIssueNumbers,
            linksRead is Result<IReadOnlyList<IssueReference>>.Failure,
            openIssues);
    }

    private TrackerContext UnreadableContext(int issueNumber, IssueContent issueContent)
    {
        try
        {
            return new TrackerContext(
                [],
                [.. ReferencedIssueSelection.Select(issueNumber, issueContent, []).Select(reference => reference.Number)],
                true,
                null);
        }
        catch (RegexMatchTimeoutException ex)
        {
            logger.LogWarning(ex, "Selecting the mentions of issue #{IssueNumber} threw.", issueNumber);
            return new TrackerContext([], [], true, null);
        }
    }

    private static async Task<Result<T>> ReadAsync<T>(
        Func<Task<Result<T>>> read,
        CancellationToken cancellationToken)
        where T : notnull
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Result<T>.Fail(TrackerReadErrors.ReadThrew(ex));
        }
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
