using System.Globalization;
using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

/// <summary>
/// The post-run rebase of a worker branch onto its target through GitLab (ADR 0087): one merge request read
/// (<c>include_diverged_commits_count</c> for how far behind it is, <c>include_rebase_in_progress</c> for the
/// rebase's own progress) and <c>PUT /projects/:id/merge_requests/:iid/rebase</c>, which GitLab runs
/// asynchronously.
/// </summary>
internal sealed partial class GitLabHttpClient
{
    /// <summary>GitLab's <c>403</c> message when the source branch refuses force pushes.</summary>
    internal const string ForcePushProtectedMessage = "Source branch is protected from force push";

    /// <summary>GitLab's <c>403</c> message when the token's user may not push to the source branch.</summary>
    internal const string CannotPushMessage = "Cannot push to source branch";

    /// <summary>GitLab's <c>403</c> message when the source branch no longer exists.</summary>
    internal const string SourceBranchMissingMessage = "Source branch does not exist";

    /// <summary>The <c>detailed_merge_status</c> GitLab reports for a merge request that conflicts with its target.</summary>
    internal const string ConflictMergeStatus = "conflict";

    public async Task<Result<PullRequestRebaseState>> GetRebaseStateAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<PullRequestRebaseState>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!TryParseMrIid(pullRequestUrl, out int mrIid))
        {
            return Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Refused(RebaseSkipReasons.NotFound));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(
            EnsureTrailingSlash(apiBaseUrl),
            $"projects/{encodedPath}/merge_requests/{mrIid}?include_diverged_commits_count=true&include_rebase_in_progress=true");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return ClassifyRebaseFailure<PullRequestRebaseState>(
                response,
                message: null,
                reason => new PullRequestRebaseState.Refused(reason));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabMergeRequestRebaseDto? dto = JsonSerializer.Deserialize<GitLabMergeRequestRebaseDto>(body, JsonOptions);

        if (dto?.Sha is not { Length: > 0 } headSha)
        {
            return Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Refused(RebaseSkipReasons.NotFound));
        }

        ProviderRebaseProgress progress = ClassifyRebaseProgress(
            dto.RebaseInProgress,
            dto.MergeError,
            dto.HasConflicts,
            dto.DetailedMergeStatus);

        int behindBy = dto.DivergedCommitsCount ?? 0;
        PullRequestRebaseState state = behindBy <= 0
            ? new PullRequestRebaseState.UpToDate(headSha) { Progress = progress }
            : new PullRequestRebaseState.Behind(mrIid.ToString(CultureInfo.InvariantCulture), headSha, behindBy) { Progress = progress };

        return Result<PullRequestRebaseState>.Ok(state);
    }

    /// <summary>
    /// Asks GitLab to rebase the merge request <paramref name="mergeRequestIid"/> onto its target. GitLab has no
    /// expected-head guard on this endpoint, so a push landing in between is simply rebased too. A <c>202</c> means
    /// the rebase was queued; it lands asynchronously.
    /// </summary>
    public async Task<Result<RebaseRequestOutcome>> RequestRebaseAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string mergeRequestIid,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<RebaseRequestOutcome>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!int.TryParse(mergeRequestIid, NumberStyles.None, CultureInfo.InvariantCulture, out int mrIid))
        {
            return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Refused(RebaseSkipReasons.NotFound));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/merge_requests/{mrIid}/rebase");

        using HttpRequestMessage request = new(HttpMethod.Put, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string? message = response.StatusCode == HttpStatusCode.Forbidden
                ? await ReadErrorMessageAsync(response, cancellationToken)
                : null;

            return ClassifyRebaseFailure<RebaseRequestOutcome>(
                response,
                message,
                reason => new RebaseRequestOutcome.Refused(reason));
        }

        RecordRestHeadroom(response);
        return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested());
    }

    /// <summary>
    /// Classifies GitLab's report of a merge request's rebase. While <c>rebase_in_progress</c> is true the rebase
    /// is still running. Once it is false, a <c>null</c> <c>merge_error</c> means it finished cleanly. GitLab
    /// writes one generic <c>merge_error</c> for every rebase failure (its text mentions conflicts whether or not
    /// there are any), so the message is not a conflict signal: a conflict is recognised from the merge
    /// request's own conflict state (<c>has_conflicts</c> or <c>detailed_merge_status: conflict</c>), and any
    /// other failure is <see cref="RebaseSkipReasons.RebaseFailed"/>, which fails open.
    /// </summary>
    internal static ProviderRebaseProgress ClassifyRebaseProgress(
        bool? rebaseInProgress,
        string? mergeError,
        bool? hasConflicts,
        string? detailedMergeStatus)
    {
        if (rebaseInProgress is true)
        {
            return new ProviderRebaseProgress.InProgress();
        }

        if (string.IsNullOrWhiteSpace(mergeError))
        {
            return new ProviderRebaseProgress.Completed();
        }

        bool conflicts = hasConflicts is true
            || string.Equals(detailedMergeStatus, ConflictMergeStatus, StringComparison.OrdinalIgnoreCase);

        return conflicts
            ? new ProviderRebaseProgress.Conflicted()
            : new ProviderRebaseProgress.Failed(RebaseSkipReasons.RebaseFailed);
    }

    /// <summary>
    /// Classifies a non-success HTTP status on a rebase call. Transient (a <c>Result</c> failure, retried with the
    /// step's bounded backoff): a 5xx, a 429, a rate-limited 403, and a <c>409</c> — GitLab's "Failed to enqueue
    /// the rebase operation", which also answers while another rebase is still running. Permanent refusals: a 403
    /// is told apart by its message (force-push protection, missing source branch, or otherwise permission), 401
    /// is permission, 404 not found, and any other status an unrecognised error.
    /// </summary>
    private static Result<T> ClassifyRebaseFailure<T>(HttpResponseMessage response, string? message, Func<string, T> refused)
        where T : notnull
    {
        HttpStatusCode status = response.StatusCode;
        bool transient = (int)status >= 500
            || status == HttpStatusCode.TooManyRequests
            || status == HttpStatusCode.Conflict
            || (status == HttpStatusCode.Forbidden && IsRateLimited(response));

        if (transient)
        {
            return Result<T>.Fail(ErrorFromNonSuccess(response));
        }

        string reason = status switch
        {
            HttpStatusCode.Forbidden when Mentions(message, ForcePushProtectedMessage) => RebaseSkipReasons.ForcePushProtected,
            HttpStatusCode.Forbidden when Mentions(message, SourceBranchMissingMessage) => RebaseSkipReasons.NotFound,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => RebaseSkipReasons.PermissionDenied,
            HttpStatusCode.NotFound => RebaseSkipReasons.NotFound,
            _ => RebaseSkipReasons.UnrecognisedError,
        };

        return Result<T>.Ok(refused(reason));
    }

    private static bool Mentions(string? message, string expected) =>
        message is not null && message.Contains(expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads GitLab's <c>{"message": "..."}</c> error body, or <c>null</c> when it is absent or unreadable.</summary>
    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String
                    ? message.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record GitLabMergeRequestRebaseDto(
        string? Sha,
        int? DivergedCommitsCount,
        bool? RebaseInProgress,
        string? MergeError,
        bool? HasConflicts,
        string? DetailedMergeStatus);
}
