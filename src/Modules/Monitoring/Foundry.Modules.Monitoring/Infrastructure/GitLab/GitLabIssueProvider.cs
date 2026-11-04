using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed class GitLabIssueProvider(
    GitLabHttpClient httpClient,
    ActionableFeedbackPolicy feedbackPolicy,
    string token,
    Uri apiBaseUrl) : IIssueProvider
{
    // Checks verdicts read alongside a merge request status, keyed by merge request URL. GitLab returns
    // both from one GET, so the checks pass (status, then checks, for the same open merge request) costs
    // one fetch per merge request instead of two. Each entry is consumed once by GetChecksAsync, so a
    // verdict is never served from an earlier pass. The provider is scoped to one poll tick.
    private readonly Dictionary<string, ProviderChecks> _checksReadWithStatus = new(StringComparer.Ordinal);

    public Task<Result<IssueListing>> GetIssuesAsync(
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

    public async Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        Result<MergeRequestSnapshot> snapshot = await httpClient.GetMergeRequestSnapshotAsync(
            apiBaseUrl,
            slug,
            pullRequestUrl,
            token,
            cancellationToken);

        if (snapshot is not Result<MergeRequestSnapshot>.Success success)
        {
            _checksReadWithStatus.Remove(pullRequestUrl);
            return Result<PullRequestStatus>.Fail(((Result<MergeRequestSnapshot>.Failure)snapshot).Error);
        }

        _checksReadWithStatus[pullRequestUrl] = success.Value.Checks;
        return Result<PullRequestStatus>.Ok(success.Value.Status);
    }

    public async Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ProviderComment>> rawResult = await httpClient.GetPullRequestReviewFeedbackAsync(
            apiBaseUrl, slug, pullRequestUrl, token, cancellationToken);

        if (rawResult is not Result<IReadOnlyList<ProviderComment>>.Success rawSuccess)
        {
            Error error = ((Result<IReadOnlyList<ProviderComment>>.Failure)rawResult).Error;
            return Result<ReviewFeedback>.Fail(error);
        }

        return Result<ReviewFeedback>.Ok(feedbackPolicy.Apply(rawSuccess.Value, since));
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

    public async Task<Result> ApplyBranchRulesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        Result<string> defaultBranchResult = await GetDefaultBranchAsync(slug, cancellationToken);

        if (defaultBranchResult is not Result<string>.Success defaultBranchSuccess)
        {
            return Result.Fail(((Result<string>.Failure)defaultBranchResult).Error);
        }

        return await httpClient.ApplyBranchRulesAsync(
            apiBaseUrl,
            slug,
            defaultBranchSuccess.Value,
            token,
            cancellationToken);
    }

    public Task<Result<bool>> CreateBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        return httpClient.CreateBranchAsync(apiBaseUrl, slug, branchName, token, cancellationToken);
    }

    public async Task<Result<bool>> DeleteBranchAsync(
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

        if (string.Equals(defaultBranchSuccess.Value, branchName, StringComparison.Ordinal))
        {
            return Result<bool>.Fail(BranchDeletionErrors.DefaultBranch(branchName));
        }

        return await httpClient.DeleteBranchAsync(apiBaseUrl, slug, branchName, token, cancellationToken);
    }

    public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        return httpClient.GetMergeRequestByBranchAsync(apiBaseUrl, slug, branchName, token, cancellationToken);
    }

    public async Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
        RepositorySlug slug,
        string branchName,
        CancellationToken cancellationToken)
    {
        Result<string> defaultBranchResult = await GetDefaultBranchAsync(slug, cancellationToken);

        if (defaultBranchResult is not Result<string>.Success defaultBranchSuccess)
        {
            Error error = ((Result<string>.Failure)defaultBranchResult).Error;
            return Result<BranchCommitSummary>.Fail(error);
        }

        return await httpClient.GetBranchCommitSummaryAsync(
            apiBaseUrl,
            slug,
            defaultBranchSuccess.Value,
            branchName,
            token,
            cancellationToken);
    }

    public Task<Result<bool>> CanPushAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetPushPermissionAsync(apiBaseUrl, slug, token, cancellationToken);
    }

    public Task<Result<ProviderChecks>> GetChecksAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        if (_checksReadWithStatus.Remove(pullRequestUrl, out ProviderChecks? checks))
        {
            return Task.FromResult(Result<ProviderChecks>.Ok(checks));
        }

        return httpClient.GetChecksAsync(apiBaseUrl, slug, pullRequestUrl, token, cancellationToken);
    }

    private Task<Result<string>> GetDefaultBranchAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetDefaultBranchAsync(apiBaseUrl, slug, token, cancellationToken);
    }
}
