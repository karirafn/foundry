using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed class GitHubIssueProvider(GitHubHttpClient httpClient, string token, Uri apiBaseUrl) : IIssueProvider
{
    // Per-poll cache: populated by GetIssuesAsync; null until the first call.
    // Lives on the instance because GitHubIssueProvider is newed per poll cycle.
    private Dictionary<int, IReadOnlyList<int>>? _blockedByByIssueNumber;

    public async Task<Result<IssueListing>> GetIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        Result<IssueListingWithDependencies> result = await httpClient.GetIssuesWithDependenciesAsync(
            apiBaseUrl, slug, token, cancellationToken);

        if (result is not Result<IssueListingWithDependencies>.Success success)
        {
            Error error = ((Result<IssueListingWithDependencies>.Failure)result).Error;
            return Result<IssueListing>.Fail(error);
        }

        _blockedByByIssueNumber = new Dictionary<int, IReadOnlyList<int>>(success.Value.BlockedBy);
        return Result<IssueListing>.Ok(success.Value.Listing);
    }

    public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        if (_blockedByByIssueNumber is null)
        {
            // No issue fetch this instance — fall back to REST for defensive correctness.
            return httpClient.GetDependenciesAsync(apiBaseUrl, slug, issueNumber, token, cancellationToken);
        }

        if (_blockedByByIssueNumber.TryGetValue(issueNumber, out IReadOnlyList<int>? blockers))
        {
            return Task.FromResult(Result<IReadOnlyList<int>>.Ok(blockers));
        }

        // Issue was fetched but had no same-repo non-closed blockers.
        return Task.FromResult(Result<IReadOnlyList<int>>.Ok((IReadOnlyList<int>)[]));
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

    public async Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        // TODO(#497 Step 4): invoke ActionableFeedbackPolicy here instead of the inline bridge below.
        Result<IReadOnlyList<ProviderComment>> rawResult = await httpClient.GetPullRequestReviewFeedbackAsync(
            apiBaseUrl, slug, pullRequestUrl, token, cancellationToken);

        if (rawResult is not Result<IReadOnlyList<ProviderComment>>.Success rawSuccess)
        {
            Error error = ((Result<IReadOnlyList<ProviderComment>>.Failure)rawResult).Error;
            return Result<ReviewFeedback>.Fail(error);
        }

        IReadOnlyList<ReviewComment> comments = rawSuccess.Value
            .Where(c => c.CreatedAt > since)
            .Select(c => new ReviewComment(c.Body, c.FilePath, c.Line))
            .ToList();

        return Result<ReviewFeedback>.Ok(new ReviewFeedback(comments, OmittedCommentCount: 0, NewestCommentAt: null));
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

        Result<BranchRules> rulesResult = await httpClient.GetBranchRulesAsync(
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

    public async Task<Result<bool>> CreateBranchAsync(
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

        return await httpClient.CreateBranchAsync(
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

    private Task<Result<string>> GetDefaultBranchAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetDefaultBranchAsync(apiBaseUrl, slug, token, cancellationToken);
    }
}
