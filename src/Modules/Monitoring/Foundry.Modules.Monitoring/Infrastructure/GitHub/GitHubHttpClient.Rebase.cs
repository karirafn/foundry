using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

/// <summary>
/// The post-run rebase of a worker branch onto its base through GitHub (ADR 0087): a GraphQL read of the
/// pull request head plus a REST compare for <c>behind_by</c>, and the <c>updatePullRequestBranch</c>
/// mutation with <c>updateMethod: REBASE</c>.
/// </summary>
internal sealed partial class GitHubHttpClient
{
    /// <summary>GitHub's error message when the rebase conflicts with the base branch.</summary>
    internal const string RebaseConflictMessage = "PullRequest::RebaseConflictError";

    /// <summary>GitHub's error message when the base branch has nothing new to rebase onto.</summary>
    internal const string NoNewCommitsMessage = "There are no new commits on the base branch.";

    /// <summary>GitHub's error message when <c>expectedHeadOid</c> no longer matches the branch head.</summary>
    internal const string StaleHeadMessage = "head sha didn't match the current head ref.";

    private const string PrRebaseStateQuery = """
        query PrRebaseState($owner: String!, $name: String!, $number: Int!) {
          rateLimit { cost remaining limit resetAt }
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) { id headRefOid baseRefName }
          }
        }
        """;

    private const string UpdatePullRequestBranchMutation = """
        mutation UpdatePullRequestBranch($pullRequestId: ID!, $expectedHeadOid: GitObjectID!) {
          updatePullRequestBranch(input: {pullRequestId: $pullRequestId, updateMethod: REBASE, expectedHeadOid: $expectedHeadOid}) {
            pullRequest { headRefOid }
          }
        }
        """;

    public async Task<Result<PullRequestRebaseState>> GetRebaseStateAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<PullRequestRebaseState>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Refused(RebaseSkipReasons.NotFound));
        }

        object variables = new { owner = slug.Owner, name = slug.Name, number = prNumber };
        Result<PrRebaseStateData> dataResult = await ExecuteGraphQlWithUnauthorizedMappingAsync<PrRebaseStateData>(
            apiBaseUrl, PrRebaseStateQuery, variables, token, cancellationToken);

        if (dataResult is not Result<PrRebaseStateData>.Success dataSuccess)
        {
            Error error = ((Result<PrRebaseStateData>.Failure)dataResult).Error;
            return string.Equals(error.Code, GitHubErrors.Unreadable.Code, StringComparison.Ordinal)
                ? Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Refused(RebaseSkipReasons.PermissionDenied))
                : Result<PullRequestRebaseState>.Fail(error);
        }

        PrRebaseStatePullRequest? pr = dataSuccess.Value.Repository?.PullRequest;
        if (pr?.Id is not { Length: > 0 } pullRequestId
            || pr.HeadRefOid is not { Length: > 0 } headSha
            || pr.BaseRefName is not { Length: > 0 } baseRefName)
        {
            return Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Refused(RebaseSkipReasons.NotFound));
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string basehead = $"{Uri.EscapeDataString(baseRefName)}...{Uri.EscapeDataString(headSha)}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/compare/{basehead}?per_page=1");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return ClassifyRestRebaseFailure<PullRequestRebaseState>(
                response,
                reason => new PullRequestRebaseState.Refused(reason));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubRebaseCompareDto? compare = JsonSerializer.Deserialize<GitHubRebaseCompareDto>(body, JsonOptions);
        int behindBy = compare?.BehindBy ?? 0;

        return behindBy <= 0
            ? Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.UpToDate(headSha))
            : Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(pullRequestId, headSha, behindBy));
    }

    public async Task<Result<RebaseRequestOutcome>> RequestRebaseAsync(
        Uri apiBaseUrl,
        string pullRequestId,
        string expectedHeadSha,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<RebaseRequestOutcome>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        Uri graphQlEndpoint = DeriveGraphQlEndpoint(apiBaseUrl);
        object variables = new { pullRequestId, expectedHeadOid = expectedHeadSha };
        string requestBody = JsonSerializer.Serialize(
            new { query = UpdatePullRequestBranchMutation, variables },
            GraphQlJsonOptions);

        using HttpRequestMessage request = new(HttpMethod.Post, graphQlEndpoint);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return ClassifyRestRebaseFailure<RebaseRequestOutcome>(
                response,
                reason => new RebaseRequestOutcome.Refused(reason));
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        GraphQlEnvelope<UpdatePullRequestBranchData>? envelope =
            JsonSerializer.Deserialize<GraphQlEnvelope<UpdatePullRequestBranchData>>(responseBody, GraphQlJsonOptions);

        if (envelope?.Errors is { Count: > 0 } errors)
        {
            return ClassifyRebaseErrors(errors.Select(e => (e.Type, e.Message)).ToList());
        }

        if (envelope?.Data?.UpdatePullRequestBranch is null)
        {
            return Result<RebaseRequestOutcome>.Fail(
                GitHubErrors.ProviderError("updatePullRequestBranch returned neither data nor errors."));
        }

        return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested());
    }

    /// <summary>
    /// Classifies the GraphQL errors of an <c>updatePullRequestBranch</c> call. GitHub reports every refusal
    /// as <c>type: UNPROCESSABLE</c> and tells them apart only by message (probed 2026-11-04), so the
    /// contract is string-typed: an unrecognised message is a permanent <c>unrecognised-error</c> refusal,
    /// which fails open. Rate limiting is the one transient error.
    /// </summary>
    internal static Result<RebaseRequestOutcome> ClassifyRebaseErrors(IReadOnlyList<(string? Type, string Message)> errors)
    {
        if (errors.Any(e => string.Equals(e.Type, "RATE_LIMITED", StringComparison.OrdinalIgnoreCase)))
        {
            return Result<RebaseRequestOutcome>.Fail(GitHubErrors.RateLimitExhausted);
        }

        if (errors.Any(e => e.Message.Contains(RebaseConflictMessage, StringComparison.Ordinal)))
        {
            return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Conflict());
        }

        if (errors.Any(e => e.Message.Contains(NoNewCommitsMessage, StringComparison.Ordinal)))
        {
            return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.UpToDate());
        }

        if (errors.Any(e => e.Message.Contains(StaleHeadMessage, StringComparison.Ordinal)))
        {
            return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.StaleHead());
        }

        if (errors.Any(e => string.Equals(e.Type, "FORBIDDEN", StringComparison.OrdinalIgnoreCase)))
        {
            return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Refused(RebaseSkipReasons.PermissionDenied));
        }

        if (errors.Any(e => string.Equals(e.Type, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)))
        {
            return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Refused(RebaseSkipReasons.NotFound));
        }

        return Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Refused(RebaseSkipReasons.UnrecognisedError));
    }

    /// <summary>
    /// Classifies a non-success HTTP status on a rebase call: a rate limit, a 5xx or a 429 is transient
    /// (a <c>Result</c> failure, retried); 401/403 is a permission refusal, 404 not found, and any other
    /// status a permanent unrecognised refusal.
    /// </summary>
    private static Result<T> ClassifyRestRebaseFailure<T>(HttpResponseMessage response, Func<string, T> refused)
        where T : notnull
    {
        HttpStatusCode status = response.StatusCode;
        bool transient = (int)status >= 500
            || status == HttpStatusCode.TooManyRequests
            || (status == HttpStatusCode.Forbidden && IsRateLimited(response));

        if (transient)
        {
            return Result<T>.Fail(ErrorFromNonSuccess(response));
        }

        string reason = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => RebaseSkipReasons.PermissionDenied,
            HttpStatusCode.NotFound => RebaseSkipReasons.NotFound,
            _ => RebaseSkipReasons.UnrecognisedError,
        };

        return Result<T>.Ok(refused(reason));
    }

    private sealed record PrRebaseStateData(GraphQlRateLimit? RateLimit, PrRebaseStateRepository? Repository);

    private sealed record PrRebaseStateRepository(PrRebaseStatePullRequest? PullRequest);

    private sealed record PrRebaseStatePullRequest(string? Id, string? HeadRefOid, string? BaseRefName);

    private sealed record UpdatePullRequestBranchData(UpdatePullRequestBranchPayload? UpdatePullRequestBranch);

    private sealed record UpdatePullRequestBranchPayload(UpdatePullRequestBranchPullRequest? PullRequest);

    private sealed record UpdatePullRequestBranchPullRequest(string? HeadRefOid);

    private sealed record GitHubRebaseCompareDto(int? BehindBy, int? AheadBy);
}
