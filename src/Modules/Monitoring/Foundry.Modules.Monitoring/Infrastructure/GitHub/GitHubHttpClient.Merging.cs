using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

/// <summary>
/// Foundry's own merge of a pull request it opened, pinned to the head its checks gate verified (ADR 0105):
/// a read of the repository's allowed merge methods on every merge (never cached, so a settings change takes
/// effect at the next merge), then <c>PUT repos/{owner}/{repo}/pulls/{number}/merge</c> with that method and
/// <c>sha</c>. Status codes follow GitHub's REST docs for "Merge a pull request"
/// (https://docs.github.com/en/rest/pulls/pulls#merge-a-pull-request).
/// </summary>
internal sealed partial class GitHubHttpClient
{
    internal const string SquashMergeMethod = "squash";
    internal const string RebaseMergeMethod = "rebase";
    internal const string MergeCommitMergeMethod = "merge";

    private const int MaxMergeRefusalReasonLength = 200;

    public async Task<Result<MergeRequestOutcome>> MergePullRequestAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string pullRequestUrl,
        string expectedHeadSha,
        string token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHeadSha);

        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<MergeRequestOutcome>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        if (!TryParsePrNumber(pullRequestUrl, out int prNumber))
        {
            return Result<MergeRequestOutcome>.Fail(GitHubErrors.InvalidPullRequestUrl);
        }

        try
        {
            Result<MergeMethodChoice> choice = await ChooseMergeMethodAsync(apiBaseUrl, slug, token, cancellationToken);
            return choice switch
            {
                Result<MergeMethodChoice>.Failure failure => Result<MergeRequestOutcome>.Fail(failure.Error),
                Result<MergeMethodChoice>.Success { Value: MergeMethodChoice.Refused refused } =>
                    Result<MergeRequestOutcome>.Ok(refused.Outcome),
                Result<MergeMethodChoice>.Success { Value: MergeMethodChoice.Chosen chosen } =>
                    await PutMergeAsync(apiBaseUrl, slug, prNumber, expectedHeadSha, chosen.Method, token, cancellationToken),
                _ => throw new UnreachableException(),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Result<MergeRequestOutcome>.Fail(GitHubErrors.ProviderError(
                $"The GitHub merge of pull request #{prNumber} on {slug} could not complete: {ex.GetType().Name}."));
        }
    }

    /// <summary>
    /// Picks the first merge method the repository allows, in the order squash, rebase, merge, or <c>null</c>
    /// when it allows none. GitHub documents each <c>allow_*</c> flag as defaulting to <c>true</c>, so an absent
    /// flag counts as allowed.
    /// </summary>
    internal static string? SelectMergeMethod(bool? allowSquashMerge, bool? allowRebaseMerge, bool? allowMergeCommit)
    {
        if (allowSquashMerge ?? true)
        {
            return SquashMergeMethod;
        }

        if (allowRebaseMerge ?? true)
        {
            return RebaseMergeMethod;
        }

        return allowMergeCommit ?? true ? MergeCommitMergeMethod : null;
    }

    /// <summary>
    /// Classifies the response of the merge call. Transient (a <c>Result</c> failure): a 5xx, a 429 and a
    /// rate-limited 403. Otherwise 200 is merged, 409 a moved head, 405 a refusal whose reason is GitHub's
    /// <c>message</c>, and 401/403 unauthorized. Anything else is an unrecognised refusal, which fails open.
    /// </summary>
    internal static Result<MergeRequestOutcome> ClassifyMergeResponse(HttpResponseMessage response, string? message)
    {
        HttpStatusCode status = response.StatusCode;
        if (IsTransientMergeFailure(response))
        {
            return Result<MergeRequestOutcome>.Fail(ErrorFromNonSuccess(response));
        }

        MergeRequestOutcome outcome = status switch
        {
            HttpStatusCode.OK => new MergeRequestOutcome.Merged(),
            HttpStatusCode.Conflict => new MergeRequestOutcome.HeadMoved(),
            HttpStatusCode.MethodNotAllowed => new MergeRequestOutcome.NotMergeable(RefusalReason(message)),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new MergeRequestOutcome.Unauthorized(),
            _ => new MergeRequestOutcome.NotMergeable(MergeRequestOutcome.NotMergeable.UnrecognisedError),
        };

        return Result<MergeRequestOutcome>.Ok(outcome);
    }

    private async Task<Result<MergeMethodChoice>> ChooseMergeMethodAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (IsTransientMergeFailure(response))
        {
            return Result<MergeMethodChoice>.Fail(ErrorFromNonSuccess(response));
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Result<MergeMethodChoice>.Ok(new MergeMethodChoice.Refused(new MergeRequestOutcome.Unauthorized()));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<MergeMethodChoice>.Ok(new MergeMethodChoice.Refused(
                new MergeRequestOutcome.NotMergeable(MergeRequestOutcome.NotMergeable.UnrecognisedError)));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubMergeSettingsDto? settings = DeserializeMergeSettings(body);
        if (settings is null)
        {
            return Result<MergeMethodChoice>.Ok(new MergeMethodChoice.Refused(
                new MergeRequestOutcome.NotMergeable(MergeRequestOutcome.NotMergeable.UnrecognisedError)));
        }

        string? method = SelectMergeMethod(settings.AllowSquashMerge, settings.AllowRebaseMerge, settings.AllowMergeCommit);
        return method is null
            ? Result<MergeMethodChoice>.Ok(new MergeMethodChoice.Refused(
                new MergeRequestOutcome.NotMergeable(MergeRequestOutcome.NotMergeable.NoMergeMethodAllowed)))
            : Result<MergeMethodChoice>.Ok(new MergeMethodChoice.Chosen(method));
    }

    private async Task<Result<MergeRequestOutcome>> PutMergeAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int prNumber,
        string expectedHeadSha,
        string mergeMethod,
        string token,
        CancellationToken cancellationToken)
    {
        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/pulls/{prNumber}/merge");

        string requestBody = JsonSerializer.Serialize(new { merge_method = mergeMethod, sha = expectedHeadSha });

        using HttpRequestMessage request = new(HttpMethod.Put, requestUri);
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            RecordRestHeadroom(response);
        }

        string? message = response.StatusCode == HttpStatusCode.MethodNotAllowed
            ? DeserializeErrorBody(await response.Content.ReadAsStringAsync(cancellationToken))?.Message
            : null;

        return ClassifyMergeResponse(response, message);
    }

    private static bool IsTransientMergeFailure(HttpResponseMessage response) =>
        (int)response.StatusCode >= 500
        || response.StatusCode == HttpStatusCode.TooManyRequests
        || (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response));

    private static string RefusalReason(string? message) =>
        string.IsNullOrWhiteSpace(message)
            ? MergeRequestOutcome.NotMergeable.UnrecognisedError
            : TruncateWithEllipsis(RedactSecrets(message.Trim()), MaxMergeRefusalReasonLength);

    private static GitHubMergeSettingsDto? DeserializeMergeSettings(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<GitHubMergeSettingsDto>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The merge method to use, or the definite answer that ends the merge before the merge call.</summary>
    private abstract record MergeMethodChoice
    {
        private MergeMethodChoice()
        {
        }

        public sealed record Chosen(string Method) : MergeMethodChoice;

        public sealed record Refused(MergeRequestOutcome Outcome) : MergeMethodChoice;
    }

    private sealed record GitHubMergeSettingsDto(bool? AllowSquashMerge, bool? AllowRebaseMerge, bool? AllowMergeCommit);
}
