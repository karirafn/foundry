using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Providers;

internal sealed class PostExitProviderQueries(
    DbContext dbContext,
    IIssueProviderFactory providerFactory,
    ICredentialResolver credentialResolver) : IPostExitProviderQueries
{
    public async Task<Result<bool>> CreateBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken)
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            Error error = ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error;
            return Result<bool>.Fail(error);
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        return await provider.CreateBranchAsync(repo.Slug, branchName, cancellationToken);
    }

    public async Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken)
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            Error error = ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error;
            return Result<MergeRequestByBranch>.Fail(error);
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        return await provider.GetMergeRequestByBranchAsync(repo.Slug, branchName, cancellationToken);
    }

    public async Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken)
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            Error error = ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error;
            return Result<BranchCommitSummary>.Fail(error);
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        return await provider.GetBranchCommitSummaryAsync(repo.Slug, branchName, cancellationToken);
    }

    private async Task<Result<(IIssueProvider Provider, MonitoredRepository Repo)>> ResolveAsync(
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

        if (string.IsNullOrEmpty(credential.Token))
        {
            return Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.CredentialTokenNotConfigured(credential.Id));
        }

        IIssueProvider provider = providerFactory.CreateProvider(credential, credential.Token);
        return Result<(IIssueProvider, MonitoredRepository)>.Ok((provider, repo));
    }
}

internal static class PostExitProviderQueriesErrors
{
    public static Error RepositoryNotFound(MonitoredRepositoryId id) =>
        new("PostExitProviderQueries.RepositoryNotFound",
            $"No monitored repository found with id '{id.Value}'.");

    public static Error CredentialNotFound(MonitoredRepositoryId id) =>
        new("PostExitProviderQueries.CredentialNotFound",
            $"No credential covers the repository with id '{id.Value}'.");

    public static Error CredentialTokenNotConfigured(CredentialId id) =>
        new("PostExitProviderQueries.CredentialTokenNotConfigured",
            $"Credential with id '{id.Value}' has no token configured.");
}
