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
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabIssueProvider(
    GitLabHttpClient httpClient,
    ActionableFeedbackPolicy feedbackPolicy,
    string token,
    Uri apiBaseUrl) : IIssueProvider
{
    // Merge request snapshots read for a status, keyed by merge request URL. GitLab returns the status and
    // the head pipeline from one GET, so the checks pass (status, then checks, for the same open merge
    // request) fetches the merge request once instead of twice. The snapshot holds only the head pipeline;
    // its failed jobs are read when GetChecksAsync consumes the verdict, so status reads never pay for them.
    // Each entry is consumed once, so a verdict is never served from an earlier pass. The provider is
    // scoped to one poll tick.
    private readonly Dictionary<string, MergeRequestSnapshot> _snapshotsReadWithStatus = new(StringComparer.Ordinal);

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

    public Task<Result<ProviderReferencedIssue>> GetReferencedIssueAsync(
        RepositorySlug slug,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        return httpClient.GetReferencedIssueAsync(apiBaseUrl, slug, issueNumber, token, cancellationToken);
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
            _snapshotsReadWithStatus.Remove(pullRequestUrl);
            return Result<PullRequestStatus>.Fail(((Result<MergeRequestSnapshot>.Failure)snapshot).Error);
        }

        _snapshotsReadWithStatus[pullRequestUrl] = success.Value;
        return Result<PullRequestStatus>.Ok(success.Value.Status);
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
        Result<IReadOnlyList<ProviderComment>> rawResult = await httpClient.GetIssueNotesAsync(
            apiBaseUrl, slug, issueNumber, token, cancellationToken);

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

        Result<IReadOnlyList<ProviderComment>> commentsResult = await httpClient.GetIssueNotesAsync(
            apiBaseUrl, slug, issueNumber, token, cancellationToken);

        if (commentsResult is not Result<IReadOnlyList<ProviderComment>>.Success comments)
        {
            return Result<ProviderIssueContent>.Fail(((Result<IReadOnlyList<ProviderComment>>.Failure)commentsResult).Error);
        }

        return Result<ProviderIssueContent>.Ok(new ProviderIssueContent(
            text.Value.Title,
            text.Value.Body,
            [.. comments.Value
                .Where(comment => !comment.IsSystem)
                .OrderBy(comment => comment.CreatedAt)]));
    }

    public Task<Result<ProviderCommentId>> PostCommentAsync(
        RepositorySlug slug,
        CommentThread thread,
        string body,
        CancellationToken cancellationToken)
    {
        return httpClient.PostNoteAsync(apiBaseUrl, slug, thread, body, token, cancellationToken);
    }

    public Task<Result> AddBlockerAsync(
        RepositorySlug slug,
        int issueNumber,
        int blockerNumber,
        CancellationToken cancellationToken)
    {
        return httpClient.AddBlockerAsync(apiBaseUrl, slug, issueNumber, blockerNumber, token, cancellationToken);
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
            return Result<string>.Fail(RunPublishingErrors.Permanent("A merge request title must not be empty."));
        }

        Result<string> defaultBranchResult = await GetDefaultBranchAsync(slug, cancellationToken);

        if (defaultBranchResult is not Result<string>.Success defaultBranchSuccess)
        {
            return defaultBranchResult;
        }

        Result<PullRequestCreation> creation = await httpClient.CreateMergeRequestAsync(
            apiBaseUrl, slug, defaultBranchSuccess.Value, sourceBranch, title, body, token, cancellationToken);

        return creation switch
        {
            Result<PullRequestCreation>.Success { Value: PullRequestCreation.Opened opened } =>
                Result<string>.Ok(opened.WebUrl),
            Result<PullRequestCreation>.Success { Value: PullRequestCreation.AlreadyOpen } =>
                await FindOpenMergeRequestAsync(slug, sourceBranch, cancellationToken),
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

    public Task<Result<WebhookUpsertKind>> UpsertWebhookAsync(
        RepositorySlug slug,
        WebhookHookSpec spec,
        CancellationToken cancellationToken)
    {
        return httpClient.UpsertWebhookAsync(apiBaseUrl, slug, spec, token, cancellationToken);
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
        if (_snapshotsReadWithStatus.Remove(pullRequestUrl, out MergeRequestSnapshot? snapshot))
        {
            return httpClient.GetPipelineChecksAsync(
                apiBaseUrl,
                slug,
                snapshot.HeadPipelineId,
                snapshot.HeadPipelineStatus,
                token,
                cancellationToken);
        }

        return httpClient.GetChecksAsync(apiBaseUrl, slug, pullRequestUrl, token, cancellationToken);
    }

    // GitLab rebase (PUT /merge_requests/:iid/rebase) follows in a separate issue (ADR 0087); until then the
    // rebase step resolves Skipped(unsupported) without any provider call.
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
        // GitLab's rebase endpoint has no expected-head guard; the merge request IID is the pull request id.
        return httpClient.RequestRebaseAsync(apiBaseUrl, slug, pullRequestId, token, cancellationToken);
    }

    private async Task<Result<string>> FindOpenMergeRequestAsync(
        RepositorySlug slug,
        string sourceBranch,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>> open = await httpClient.ListOpenMergeRequestUrlsAsync(
            apiBaseUrl, slug, sourceBranch, token, cancellationToken);

        return open switch
        {
            Result<IReadOnlyList<string>>.Success { Value: [string url, ..] } => Result<string>.Ok(url),
            Result<IReadOnlyList<string>>.Success => Result<string>.Fail(RunPublishingErrors.Permanent(
                $"GitLab reports a merge request already exists for '{sourceBranch}' on {slug}, but none is open.")),
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

    // Check-to-job mapping. A GitLab CI job's web page is https://{host}/{project path}/-/jobs/{job_id}, and the
    // {job_id} in it is the `id` that "Get a log file" (GET /projects/:id/jobs/:job_id/trace) takes — the Jobs API
    // returns that same page as the job's `web_url`. No provider call is needed. A link to any other page, or to a
    // job of another project, is not a job whose log Foundry reads through this repository.
    public Task<Result<CiJobLookup>> ResolveCiJobAsync(
        RepositorySlug slug,
        FailingCheck check,
        CancellationToken cancellationToken)
    {
        CiJobLookup lookup = TryParseJobId(slug, check.DetailsUrl, out long jobId)
            ? new CiJobLookup.Found(jobId)
            : new CiJobLookup.NotACiJob();
        return Task.FromResult(Result<CiJobLookup>.Ok(lookup));
    }

    public Task<Result<string>> GetJobLogAsync(
        RepositorySlug slug,
        long jobId,
        CancellationToken cancellationToken)
    {
        return httpClient.GetJobTraceAsync(apiBaseUrl, slug, jobId, token, cancellationToken);
    }

    private static bool TryParseJobId(RepositorySlug slug, string? detailsUrl, out long jobId)
    {
        jobId = 0;
        if (!Uri.TryCreate(detailsUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        Match match = JobPagePattern().Match(uri.AbsolutePath);
        return match.Success
            && string.Equals(Uri.UnescapeDataString(match.Groups["path"].Value), slug.FullPath, StringComparison.OrdinalIgnoreCase)
            && long.TryParse(match.Groups["job"].Value, out jobId);
    }

    [GeneratedRegex(@"^/(?<path>.+)/-/jobs/(?<job>\d+)/?$")]
    private static partial Regex JobPagePattern();
}
