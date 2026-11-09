using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Providers;

internal sealed class PostExitProviderQueries(
    DbContext dbContext,
    IIssueProviderFactory providerFactory,
    ICredentialResolver credentialResolver)
    : IPostExitProviderQueries, IWorkBranchCommands, IIssueBlockerQueries, IFailingJobLogQueries
{
    public async Task<Result<IReadOnlyList<int>>> GetOpenBlockersAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            Error error = ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error;
            return Result<IReadOnlyList<int>>.Fail(error);
        }

        // A provider created here has performed no issue listing, so GitHub answers from REST
        // rather than a listing cache — the read is live.
        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        return await provider.GetDependenciesAsync(repo.Slug, issueNumber, cancellationToken);
    }

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

    public async Task<Result<bool>> DeleteBranchAsync(
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
        return await provider.DeleteBranchAsync(repo.Slug, branchName, cancellationToken);
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

    public async Task<Result<CiJobLog>> GetFailingJobLogAsync(
        MonitoredRepositoryId repositoryId,
        FailingCheck check,
        CancellationToken cancellationToken)
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            Error error = ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error;
            return Result<CiJobLog>.Fail(error);
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;
        Result<CiJobLookup> lookup = await provider.ResolveCiJobAsync(repo.Slug, check, cancellationToken);

        if (lookup is Result<CiJobLookup>.Failure lookupFailure)
        {
            return Result<CiJobLog>.Fail(lookupFailure.Error);
        }

        if (lookup is not Result<CiJobLookup>.Success { Value: CiJobLookup.Found found })
        {
            return Result<CiJobLog>.Ok(new CiJobLog.NotACiJob());
        }

        Result<string> log = await provider.GetJobLogAsync(repo.Slug, found.JobId, cancellationToken);
        return log switch
        {
            Result<string>.Success success => Result<CiJobLog>.Ok(new CiJobLog.Available(success.Value)),
            Result<string>.Failure failure => Result<CiJobLog>.Fail(failure.Error),
            _ => throw new UnreachableException(),
        };
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

        if (credential.Token is ProviderToken.Unreadable)
        {
            return Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.CredentialTokenUnreadable(credential.Id));
        }

        if (credential.Token is not ProviderToken.Present present)
        {
            return Result<(IIssueProvider, MonitoredRepository)>.Fail(
                PostExitProviderQueriesErrors.CredentialTokenNotConfigured(credential.Id));
        }

        IIssueProvider provider = providerFactory.CreateProvider(credential, present.Value);
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

    public static Error CredentialTokenUnreadable(CredentialId id) =>
        new("PostExitProviderQueries.CredentialTokenUnreadable",
            $"Credential with id '{id.Value}' has an unreadable token — the stored ciphertext could not be decrypted.");
}
