using System.Diagnostics;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Publishing;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubIssueProvider(
    GitHubHttpClient httpClient,
    GitHubJobLogClient jobLogClient,
    ActionableFeedbackPolicy feedbackPolicy,
    string token,
    Uri apiBaseUrl) : IIssueProvider
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

    public Task<Result<IReadOnlyList<IssueReference>>> GetIssueLinksAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        return httpClient.GetIssueLinksAsync(apiBaseUrl, slug, issueNumber, token, cancellationToken);
    }

    public Task<Result<IReadOnlyList<OpenIssue>>> GetOpenIssuesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetOpenIssuesAsync(apiBaseUrl, slug, token, cancellationToken);
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
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ProviderComment>> rawResult = await httpClient.GetPullRequestReviewFeedbackAsync(
            apiBaseUrl, slug, pullRequestUrl, token, cancellationToken);

        if (rawResult is not Result<IReadOnlyList<ProviderComment>>.Success rawSuccess)
        {
            Error error = ((Result<IReadOnlyList<ProviderComment>>.Failure)rawResult).Error;
            return Result<ReviewFeedback>.Fail(error);
        }

        return Result<ReviewFeedback>.Ok(feedbackPolicy.Apply(rawSuccess.Value, since, postedComments));
    }

    public async Task<Result<IReadOnlyList<ProviderComment>>> GetIssueFeedbackAsync(
        RepositorySlug slug,
        int issueNumber,
        DateTimeOffset since,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ProviderComment>> rawResult = await httpClient.GetIssueCommentsAsync(
            apiBaseUrl, slug, issueNumber, since, token, cancellationToken);

        return rawResult is Result<IReadOnlyList<ProviderComment>>.Success rawSuccess
            ? Result<IReadOnlyList<ProviderComment>>.Ok(
                IssueFeedbackSelection.Apply(rawSuccess.Value, since, postedComments))
            : rawResult;
    }

    public async Task<Result<ProviderIssueContent>> GetIssueContentAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        Result<ProviderIssueText> textResult = await httpClient.GetIssueTextAsync(
            apiBaseUrl, slug, issueNumber, token, cancellationToken);

        if (textResult is not Result<ProviderIssueText>.Success text)
        {
            return Result<ProviderIssueContent>.Fail(((Result<ProviderIssueText>.Failure)textResult).Error);
        }

        Result<IReadOnlyList<ProviderComment>> commentsResult = await httpClient.GetIssueCommentsAsync(
            apiBaseUrl, slug, issueNumber, DateTimeOffset.UnixEpoch, token, cancellationToken);

        if (commentsResult is not Result<IReadOnlyList<ProviderComment>>.Success comments)
        {
            return Result<ProviderIssueContent>.Fail(((Result<IReadOnlyList<ProviderComment>>.Failure)commentsResult).Error);
        }

        return Result<ProviderIssueContent>.Ok(new ProviderIssueContent(
            text.Value.Title,
            text.Value.Body,
            [.. comments.Value
                .OrderBy(comment => comment.CreatedAt)]));
    }

    public Task<Result<ProviderCommentId>> PostCommentAsync(
        RepositorySlug slug,
        CommentThread thread,
        string body,
        CancellationToken cancellationToken)
    {
        return httpClient.PostIssueCommentAsync(apiBaseUrl, slug, thread, body, token, cancellationToken);
    }

    public async Task<Result<string>> CreatePullRequestAsync(
        RepositorySlug slug,
        string sourceBranch,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Result<string>.Fail(RunPublishingErrors.Permanent("A pull request title must not be empty."));
        }

        Result<string> defaultBranchResult = await GetDefaultBranchAsync(slug, cancellationToken);

        if (defaultBranchResult is not Result<string>.Success defaultBranchSuccess)
        {
            return defaultBranchResult;
        }

        Result<PullRequestCreation> creation = await httpClient.CreatePullRequestAsync(
            apiBaseUrl, slug, defaultBranchSuccess.Value, sourceBranch, title, body, token, cancellationToken);

        return creation switch
        {
            Result<PullRequestCreation>.Success { Value: PullRequestCreation.Opened opened } =>
                Result<string>.Ok(opened.WebUrl),
            Result<PullRequestCreation>.Success { Value: PullRequestCreation.AlreadyOpen } =>
                await FindOpenPullRequestAsync(slug, sourceBranch, cancellationToken),
            Result<PullRequestCreation>.Failure failure => Result<string>.Fail(failure.Error),
            _ => throw new UnreachableException(),
        };
    }

    public Task<Result<CommitIdentity>> GetAuthenticatedIdentityAsync(CancellationToken cancellationToken)
    {
        return httpClient.GetAuthenticatedIdentityAsync(apiBaseUrl, token, cancellationToken);
    }

    public Task<Result<bool>> IsAncestorAsync(
        RepositorySlug slug,
        string ancestorRef,
        string branchName,
        CancellationToken cancellationToken)
    {
        return httpClient.IsAncestorAsync(apiBaseUrl, slug, ancestorRef, branchName, token, cancellationToken);
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

    public Task<Result> ApplyBranchRulesAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.ApplyBranchRulesAsync(apiBaseUrl, slug, token, cancellationToken);
    }

    public Task<Result<WebhookUpsertKind>> UpsertWebhookAsync(
        RepositorySlug slug,
        WebhookHookSpec spec,
        CancellationToken cancellationToken)
    {
        return httpClient.UpsertWebhookAsync(apiBaseUrl, slug, spec, token, cancellationToken);
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
        return httpClient.GetChecksAsync(apiBaseUrl, slug, pullRequestUrl, token, cancellationToken);
    }

    // Check-to-job mapping. GitHub's docs do not state that a check run ID equals an Actions job ID, so the ID is
    // never taken from the check itself. A check created by GitHub Actions links (details_url) to its job page,
    // https://{host}/{owner}/{repo}/actions/runs/{run_id}/job/{job_id}. The run ID from that link selects the run;
    // "List jobs for a workflow run" (GET /repos/{owner}/{repo}/actions/runs/{run_id}/jobs) then names the job whose
    // html_url is that same page — its `id` is the job ID "Download job logs for a workflow run"
    // (GET /repos/{owner}/{repo}/actions/jobs/{job_id}/logs) takes. When no html_url matches, a single job with
    // the check's name is used: an Actions check run carries its job's name. Any other check — a commit status, a
    // third-party check app, a run in another repository — is not a job whose log Foundry can read.
    public async Task<Result<CiJobLookup>> ResolveCiJobAsync(
        RepositorySlug slug,
        FailingCheck check,
        CancellationToken cancellationToken)
    {
        if (!TryParseActionsRunId(slug, check.DetailsUrl, out long runId))
        {
            return Result<CiJobLookup>.Ok(new CiJobLookup.NotACiJob());
        }

        Result<IReadOnlyList<WorkflowRunJob>> jobsResult =
            await httpClient.ListWorkflowRunJobsAsync(apiBaseUrl, slug, runId, token, cancellationToken);

        if (jobsResult is not Result<IReadOnlyList<WorkflowRunJob>>.Success jobsSuccess)
        {
            return Result<CiJobLookup>.Fail(((Result<IReadOnlyList<WorkflowRunJob>>.Failure)jobsResult).Error);
        }

        IReadOnlyList<WorkflowRunJob> jobs = jobsSuccess.Value;
        WorkflowRunJob? job = jobs.FirstOrDefault(candidate => IsSamePage(candidate.HtmlUrl, check.DetailsUrl));

        if (job is null)
        {
            List<WorkflowRunJob> sameName = jobs
                .Where(candidate => string.Equals(candidate.Name, check.Name, StringComparison.Ordinal))
                .ToList();
            job = sameName.Count == 1 ? sameName[0] : null;
        }

        return job is null
            ? Result<CiJobLookup>.Ok(new CiJobLookup.NotACiJob())
            : Result<CiJobLookup>.Ok(new CiJobLookup.Found(job.Id));
    }

    public Task<Result<string>> GetJobLogAsync(
        RepositorySlug slug,
        long jobId,
        CancellationToken cancellationToken)
    {
        return jobLogClient.GetJobLogAsync(apiBaseUrl, slug, jobId, token, cancellationToken);
    }

    public Task<Result<PullRequestRebaseState>> GetRebaseStateAsync(
        RepositorySlug slug,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        return httpClient.GetRebaseStateAsync(apiBaseUrl, slug, pullRequestUrl, token, cancellationToken);
    }

    public Task<Result<RebaseRequestOutcome>> RequestRebaseAsync(
        RepositorySlug slug,
        string pullRequestId,
        string expectedHeadSha,
        CancellationToken cancellationToken)
    {
        return httpClient.RequestRebaseAsync(apiBaseUrl, pullRequestId, expectedHeadSha, token, cancellationToken);
    }

    private async Task<Result<string>> FindOpenPullRequestAsync(
        RepositorySlug slug,
        string sourceBranch,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>> open = await httpClient.ListOpenPullRequestUrlsAsync(
            apiBaseUrl, slug, sourceBranch, token, cancellationToken);

        return open switch
        {
            Result<IReadOnlyList<string>>.Success { Value: [string url, ..] } => Result<string>.Ok(url),
            Result<IReadOnlyList<string>>.Success => Result<string>.Fail(RunPublishingErrors.Permanent(
                $"GitHub reports a pull request already exists for '{sourceBranch}' on {slug}, but none is open.")),
            Result<IReadOnlyList<string>>.Failure failure => Result<string>.Fail(failure.Error),
            _ => throw new UnreachableException(),
        };
    }

    private Task<Result<string>> GetDefaultBranchAsync(
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        return httpClient.GetDefaultBranchAsync(apiBaseUrl, slug, token, cancellationToken);
    }

    private static bool TryParseActionsRunId(RepositorySlug slug, string? detailsUrl, out long runId)
    {
        runId = 0;
        if (!Uri.TryCreate(detailsUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        Match match = ActionsJobPagePattern().Match(uri.AbsolutePath);
        return match.Success
            && string.Equals(Uri.UnescapeDataString(match.Groups["owner"].Value), slug.Owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Uri.UnescapeDataString(match.Groups["repo"].Value), slug.Name, StringComparison.OrdinalIgnoreCase)
            && long.TryParse(match.Groups["run"].Value, out runId);
    }

    // Compares host and path only: a details URL may carry a query (e.g. ?pr=12) the job's html_url lacks.
    private static bool IsSamePage(string? jobHtmlUrl, string? detailsUrl) =>
        Uri.TryCreate(jobHtmlUrl, UriKind.Absolute, out Uri? jobPage)
        && Uri.TryCreate(detailsUrl, UriKind.Absolute, out Uri? checkPage)
        && string.Equals(jobPage.Host, checkPage.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            jobPage.AbsolutePath.TrimEnd('/'),
            checkPage.AbsolutePath.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^/(?<owner>[^/]+)/(?<repo>[^/]+)/actions/runs/(?<run>\d+)/job/\d+/?$")]
    private static partial Regex ActionsJobPagePattern();
}
