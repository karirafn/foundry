using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class PostExitProviderQueries(
    DbContext dbContext,
    IIssueProviderFactory providerFactory) : IPostExitProviderQueries
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

    public async Task<Result<bool>> HasBranchCommitsAsync(
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
        return await provider.HasBranchCommitsAsync(repo.Slug, branchName, cancellationToken);
    }

    public async Task<Result<string>> GetPullRequestByBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken)
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            Error error = ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error;
            return Result<string>.Fail(error);
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        return await provider.GetPullRequestByBranchAsync(repo.Slug, branchName, cancellationToken);
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

    public async Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken)
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            Error error = ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error;
            return Result<LatestBranchCommit>.Fail(error);
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        return await provider.GetLatestBranchCommitAsync(repo.Slug, branchName, cancellationToken);
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

        Account? account = await dbContext.Set<Account>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == repo.AccountId, cancellationToken);

        if (account is null)
        {
            return Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.AccountNotFound(repo.AccountId));
        }

        if (string.IsNullOrEmpty(account.Token))
        {
            return Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.AccountTokenNotConfigured(account.Id));
        }

        IIssueProvider provider = providerFactory.CreateProvider(account, account.Token);
        return Result<(IIssueProvider, MonitoredRepository)>.Ok((provider, repo));
    }
}

internal static class PostExitProviderQueriesErrors
{
    public static Error RepositoryNotFound(MonitoredRepositoryId id) =>
        new("PostExitProviderQueries.RepositoryNotFound",
            $"No monitored repository found with id '{id.Value}'.");

    public static Error AccountNotFound(AccountId id) =>
        new("PostExitProviderQueries.AccountNotFound",
            $"No account found with id '{id.Value}'.");

    public static Error AccountTokenNotConfigured(AccountId id) =>
        new("PostExitProviderQueries.AccountTokenNotConfigured",
            $"Account with id '{id.Value}' has no token configured.");
}
