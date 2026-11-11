using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Publishing;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    private const string PullRequestAlreadyExistsPrefix = "A pull request already exists";
    private const string ApiSubdomainPrefix = "api.";

    /// <summary>
    /// Opens a pull request from <paramref name="headBranch"/> into <paramref name="baseBranch"/>
    /// (<c>POST /repos/{owner}/{repo}/pulls</c>). GitHub's 422 "A pull request already exists" answers
    /// <see cref="PullRequestCreation.AlreadyOpen"/>; every other failure is a <see cref="RunPublishingErrors"/>
    /// transient or permanent error.
    /// </summary>
    public async Task<Result<PullRequestCreation>> CreatePullRequestAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string baseBranch,
        string headBranch,
        string title,
        string body,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<PullRequestCreation>.Fail(RunPublishingErrors.Permanent(GitHubErrors.InvalidBaseUrl.Message));
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string action = $"Opening a pull request from '{headBranch}' on {slug}";

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/pulls"));
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new GitHubPullRequestWriteDto(title, headBranch, baseBranch, body), JsonOptions),
            Encoding.UTF8,
            "application/json");

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<PullRequestCreation>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.UnprocessableEntity
                && await IsPullRequestAlreadyExistsAsync(response, cancellationToken))
            {
                RecordRestHeadroom(response);
                return Result<PullRequestCreation>.Ok(new PullRequestCreation.AlreadyOpen());
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<PullRequestCreation>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitHubCreatedPullRequestDto? created =
                JsonSerializer.Deserialize<GitHubCreatedPullRequestDto>(responseBody, JsonOptions);

            // A retry finds the pull request this call created through the already-exists path.
            return string.IsNullOrWhiteSpace(created?.HtmlUrl)
                ? Result<PullRequestCreation>.Fail(RunPublishingErrors.Transient(
                    $"{action}: GitHub accepted the request but returned no pull request URL."))
                : Result<PullRequestCreation>.Ok(new PullRequestCreation.Opened(created.HtmlUrl));
        }
    }

    /// <summary>
    /// Lists the web URLs of the open pull requests whose head is <paramref name="headBranch"/> in the repository
    /// itself (<c>GET /repos/{owner}/{repo}/pulls?head={owner}:{branch}&amp;state=open</c>).
    /// </summary>
    public async Task<Result<IReadOnlyList<string>>> ListOpenPullRequestUrlsAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string headBranch,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<string>>.Fail(RunPublishingErrors.Permanent(GitHubErrors.InvalidBaseUrl.Message));
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string head = Uri.EscapeDataString($"{slug.Owner}:{headBranch}");
        string action = $"Looking up the open pull request from '{headBranch}' on {slug}";

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            new Uri(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/pulls?head={head}&state=open"));
        AddGitHubHeaders(request, token);

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<IReadOnlyList<string>>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<string>>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitHubCreatedPullRequestDto> pulls =
                JsonSerializer.Deserialize<List<GitHubCreatedPullRequestDto>>(responseBody, JsonOptions) ?? [];

            return Result<IReadOnlyList<string>>.Ok(
                [.. pulls.Select(pull => pull.HtmlUrl).OfType<string>().Where(url => url.Length > 0)]);
        }
    }

    /// <summary>
    /// Reads the token owner (<c>GET /user</c>) as a commit identity: the profile name, else the login, and the
    /// account's noreply address. The noreply address is used rather than any public email so publishing never
    /// exposes an address the account owner chose to keep private, while GitHub still attributes the commits
    /// to the account.
    /// </summary>
    public async Task<Result<CommitIdentity>> GetAuthenticatedIdentityAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<CommitIdentity>.Fail(RunPublishingErrors.Permanent(GitHubErrors.InvalidBaseUrl.Message));
        }

        const string action = "Reading the GitHub account identity";

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(EnsureTrailingSlash(apiBaseUrl), "user"));
        AddGitHubHeaders(request, token);

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<CommitIdentity>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<CommitIdentity>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitHubIdentityDto? user = JsonSerializer.Deserialize<GitHubIdentityDto>(responseBody, JsonOptions);

            if (user is not { Id: > 0 } || string.IsNullOrWhiteSpace(user.Login))
            {
                return Result<CommitIdentity>.Fail(RunPublishingErrors.Permanent(
                    $"{action}: GitHub returned no account ID or login."));
            }

            string name = string.IsNullOrWhiteSpace(user.Name) ? user.Login : user.Name.Trim();
            string email = $"{user.Id}+{user.Login}@users.noreply.{WebHostOf(apiBaseUrl)}";
            return Result<CommitIdentity>.Ok(new CommitIdentity(name, email));
        }
    }

    /// <summary>
    /// Whether <paramref name="ancestorRef"/> is an ancestor of, or equal to, the head of
    /// <paramref name="branchName"/>, read from the compare API
    /// (<c>GET /repos/{owner}/{repo}/compare/{base}...{head}</c>): status <c>ahead</c> or <c>identical</c>.
    /// A 404 means a ref is missing.
    /// </summary>
    public async Task<Result<bool>> IsAncestorAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string ancestorRef,
        string branchName,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<bool>.Fail(RunPublishingErrors.Permanent(GitHubErrors.InvalidBaseUrl.Message));
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string basePart = Uri.EscapeDataString(ancestorRef);
        string headPart = Uri.EscapeDataString(branchName);
        string action = $"Comparing '{ancestorRef}' with '{branchName}' on {slug}";

        // per_page=1: only the status is read, so the commit list is kept to one entry.
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            new Uri(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/compare/{basePart}...{headPart}?per_page=1"));
        AddGitHubHeaders(request, token);

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<bool>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                RecordRestHeadroom(response);
                return Result<bool>.Fail(RunPublishingErrors.RefNotFound($"{action}: a ref does not exist."));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<bool>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitHubCompareStatusDto? compare = JsonSerializer.Deserialize<GitHubCompareStatusDto>(responseBody, JsonOptions);

            return compare?.Status switch
            {
                "ahead" or "identical" => Result<bool>.Ok(true),
                "behind" or "diverged" => Result<bool>.Ok(false),
                _ => Result<bool>.Fail(RunPublishingErrors.Permanent(
                    $"{action}: GitHub returned an unknown compare status '{compare?.Status}'.")),
            };
        }
    }

    // github.com and GHE.com serve the API on an "api." subdomain of the web host; GitHub Enterprise Server
    // serves it under /api/v3 on the web host itself. The noreply domain is built on the web host.
    private static string WebHostOf(Uri apiBaseUrl) =>
        apiBaseUrl.Host.StartsWith(ApiSubdomainPrefix, StringComparison.OrdinalIgnoreCase)
            ? apiBaseUrl.Host[ApiSubdomainPrefix.Length..]
            : apiBaseUrl.Host;

    private async Task<Result<HttpResponseMessage>> SendPublishingRequestAsync(
        HttpRequestMessage request,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            return Result<HttpResponseMessage>.Ok(await httpClient.SendAsync(request, cancellationToken));
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result<HttpResponseMessage>.Fail(RunPublishingErrors.Transient(
                $"{action} did not complete: {ex.GetType().Name}."));
        }
    }

    private static Error ClassifyPublishingFailure(HttpResponseMessage response, string action)
    {
        string message = $"{action} returned {(int)response.StatusCode}.";
        return IsRetryableStatus(response) ? RunPublishingErrors.Transient(message) : RunPublishingErrors.Permanent(message);
    }

    private static bool IsRetryableStatus(HttpResponseMessage response) =>
        (int)response.StatusCode >= 500
        || response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
        || (response.StatusCode is HttpStatusCode.Forbidden && IsRateLimited(response));

    private static async Task<bool> IsPullRequestAlreadyExistsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            GitHubValidationFailureDto? failure = JsonSerializer.Deserialize<GitHubValidationFailureDto>(body, JsonOptions);
            return failure?.Errors?.Any(error =>
                error.Message?.StartsWith(PullRequestAlreadyExistsPrefix, StringComparison.Ordinal) == true) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record GitHubPullRequestWriteDto(string Title, string Head, string Base, string Body);

    private sealed record GitHubCreatedPullRequestDto(string? HtmlUrl);

    private sealed record GitHubValidationFailureDto(IReadOnlyList<GitHubValidationErrorDto>? Errors);

    private sealed record GitHubValidationErrorDto(string? Message);

    private sealed record GitHubIdentityDto(long? Id, string? Login, string? Name);

    private sealed record GitHubCompareStatusDto(string? Status);
}
