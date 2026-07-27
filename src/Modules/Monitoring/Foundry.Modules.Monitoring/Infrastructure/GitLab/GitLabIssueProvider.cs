using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed class GitLabIssueProvider(GitLabHttpClient httpClient, string token, Uri apiBaseUrl) : IIssueProvider
{
    public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetIssuesAsync(apiBaseUrl, slug, token, cancellationToken);
    }

    public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        return httpClient.GetDependenciesAsync(apiBaseUrl, slug, issueNumber, token, cancellationToken);
    }

    public Task<Result<bool>> IsIssueClosedAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        return httpClient.IsIssueClosedAsync(apiBaseUrl, slug, issueNumber, token, cancellationToken);
    }

    public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        return httpClient.GetPullRequestStatusAsync(apiBaseUrl, slug, pullRequestUrl, token, cancellationToken);
    }

    public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        return httpClient.GetPullRequestReviewFeedbackAsync(
            apiBaseUrl, slug, pullRequestUrl, since, token, cancellationToken);
    }

    public async Task<Result<BranchProtection>> GetBranchProtectionAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        Result<string> defaultBranchResult = await GetDefaultBranchAsync(slug, cancellationToken);

        if (defaultBranchResult is not Result<string>.Success defaultBranchSuccess)
        {
            Error error = ((Result<string>.Failure)defaultBranchResult).Error;
            return Result<BranchProtection>.Fail(error);
        }

        string defaultBranch = defaultBranchSuccess.Value;

        Result<BranchRules> rulesResult = await httpClient.GetBranchProtectionAsync(
            apiBaseUrl,
            slug,
            defaultBranch,
            token,
            cancellationToken);

        if (rulesResult is not Result<BranchRules>.Success rulesSuccess)
        {
            Error error = ((Result<BranchRules>.Failure)rulesResult).Error;
            return Result<BranchProtection>.Fail(error);
        }

        BranchRules rules = rulesSuccess.Value;
        return Result<BranchProtection>.Ok(new BranchProtection(
            defaultBranch,
            rules.RejectDirectPushes,
            rules.RejectForcePushes,
            rules.RejectDeletion));
    }

    public Task<Result<bool>> CreateBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        return httpClient.CreateBranchAsync(apiBaseUrl, slug, branchName, token, cancellationToken);
    }

    public async Task<Result<bool>> HasBranchCommitsAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        Result<string> defaultBranchResult = await GetDefaultBranchAsync(slug, cancellationToken);

        if (defaultBranchResult is not Result<string>.Success defaultBranchSuccess)
        {
            Error error = ((Result<string>.Failure)defaultBranchResult).Error;
            return Result<bool>.Fail(error);
        }

        return await httpClient.HasBranchCommitsAsync(
            apiBaseUrl,
            slug,
            defaultBranchSuccess.Value,
            branchName,
            token,
            cancellationToken);
    }

    public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        return httpClient.GetMergeRequestByBranchAsync(apiBaseUrl, slug, branchName, token, cancellationToken);
    }

    public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        return httpClient.GetLatestBranchCommitAsync(apiBaseUrl, slug, branchName, token, cancellationToken);
    }

    public Task<Result<bool>> CanPushAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetPushPermissionAsync(apiBaseUrl, slug, token, cancellationToken);
    }

    private Task<Result<string>> GetDefaultBranchAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetDefaultBranchAsync(apiBaseUrl, slug, token, cancellationToken);
    }
}
