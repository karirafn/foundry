using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

/// <summary>
/// Foundry's own merge of a merge request it opened, pinned to the head its checks gate verified (ADR 0105):
/// a read of the project's <c>squash_option</c> on every merge (never cached), then
/// <c>PUT projects/:id/merge_requests/:iid/merge</c> with <c>sha</c> and <c>squash</c>. The project's own
/// <c>merge_method</c> is never overridden. Status codes follow GitLab's "Merge a merge request" docs
/// (https://docs.gitlab.com/api/merge_requests/#merge-a-merge-request): 401 no permission, 405 cannot merge,
/// 409 SHA does not match the source head, 422 branch cannot be merged.
/// </summary>
internal sealed partial class GitLabHttpClient
{
    /// <summary>The <c>squash_option</c> under which the project refuses squashing.</summary>
    internal const string SquashOptionNever = "never";

    private const int MaxMergeRefusalReasonLength = 200;

    public async Task<Result<MergeRequestOutcome>> MergePullRequestAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string mergeRequestUrl,
        string expectedHeadSha,
        string token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHeadSha);

        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<MergeRequestOutcome>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (!TryParseMrIid(mergeRequestUrl, out int mrIid))
        {
            return Result<MergeRequestOutcome>.Fail(GitLabErrors.InvalidMergeRequestUrl);
        }

        try
        {
            Result<SquashChoice> choice = await ChooseSquashAsync(apiBaseUrl, slug, token, cancellationToken);
            return choice switch
            {
                Result<SquashChoice>.Failure failure => Result<MergeRequestOutcome>.Fail(failure.Error),
                Result<SquashChoice>.Success { Value: SquashChoice.Refused refused } =>
                    Result<MergeRequestOutcome>.Ok(refused.Outcome),
                Result<SquashChoice>.Success { Value: SquashChoice.Chosen chosen } =>
                    await PutMergeAsync(apiBaseUrl, slug, mrIid, expectedHeadSha, chosen.Squash, token, cancellationToken),
                _ => throw new UnreachableException(),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Result<MergeRequestOutcome>.Fail(GitLabErrors.ProviderError(
                $"The GitLab merge of merge request !{mrIid} on {slug} could not complete: {ex.GetType().Name}."));
        }
    }

    /// <summary>Squashes unless the project's <c>squash_option</c> is <c>never</c>.</summary>
    internal static bool ShouldSquash(string? squashOption) =>
        !string.Equals(squashOption, SquashOptionNever, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Classifies the response of the merge call by status code first and message second. Transient (a
    /// <c>Result</c> failure): a 5xx, a 429 and a rate-limited 403. Otherwise 200 is merged, 409 a moved head,
    /// 405/406/422 a refusal whose reason is GitLab's <c>message</c> (406 is the older GitLab code for 422), and
    /// 401/403 unauthorized. Anything else is an unrecognised refusal, which fails open.
    /// </summary>
    internal static Result<MergeRequestOutcome> ClassifyMergeResponse(HttpResponseMessage response, string? message)
    {
        if (IsTransientMergeFailure(response))
        {
            return Result<MergeRequestOutcome>.Fail(ErrorFromNonSuccess(response));
        }

        MergeRequestOutcome outcome = response.StatusCode switch
        {
            HttpStatusCode.OK => new MergeRequestOutcome.Merged(),
            HttpStatusCode.Conflict => new MergeRequestOutcome.HeadMoved(),
            HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotAcceptable or HttpStatusCode.UnprocessableEntity =>
                new MergeRequestOutcome.NotMergeable(RefusalReason(message)),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new MergeRequestOutcome.Unauthorized(),
            _ => new MergeRequestOutcome.NotMergeable(MergeRequestOutcome.NotMergeable.UnrecognisedError),
        };

        return Result<MergeRequestOutcome>.Ok(outcome);
    }

    private async Task<Result<SquashChoice>> ChooseSquashAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (IsTransientMergeFailure(response))
        {
            return Result<SquashChoice>.Fail(ErrorFromNonSuccess(response));
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Result<SquashChoice>.Ok(new SquashChoice.Refused(new MergeRequestOutcome.Unauthorized()));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<SquashChoice>.Ok(new SquashChoice.Refused(
                new MergeRequestOutcome.NotMergeable(MergeRequestOutcome.NotMergeable.UnrecognisedError)));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitLabSquashSettingsDto? settings = DeserializeSquashSettings(body);
        return settings is null
            ? Result<SquashChoice>.Ok(new SquashChoice.Refused(
                new MergeRequestOutcome.NotMergeable(MergeRequestOutcome.NotMergeable.UnrecognisedError)))
            : Result<SquashChoice>.Ok(new SquashChoice.Chosen(ShouldSquash(settings.SquashOption)));
    }

    private async Task<Result<MergeRequestOutcome>> PutMergeAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int mrIid,
        string expectedHeadSha,
        bool squash,
        string token,
        CancellationToken cancellationToken)
    {
        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/merge_requests/{mrIid}/merge");

        string requestBody = JsonSerializer.Serialize(new { sha = expectedHeadSha, squash });

        using HttpRequestMessage request = new(HttpMethod.Put, requestUri);
        AddCommonHeaders(request, token);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            RecordRestHeadroom(response);
        }

        string? message = response.IsSuccessStatusCode
            ? null
            : await ReadErrorMessageAsync(response, cancellationToken);

        return ClassifyMergeResponse(response, message);
    }

    private static bool IsTransientMergeFailure(HttpResponseMessage response) =>
        (int)response.StatusCode >= 500
        || response.StatusCode == HttpStatusCode.TooManyRequests
        || (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response));

    private static string RefusalReason(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return MergeRequestOutcome.NotMergeable.UnrecognisedError;
        }

        string trimmed = message.Trim();
        return trimmed.Length <= MaxMergeRefusalReasonLength ? trimmed : trimmed[..MaxMergeRefusalReasonLength];
    }

    private static GitLabSquashSettingsDto? DeserializeSquashSettings(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<GitLabSquashSettingsDto>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether to squash, or the definite answer that ends the merge before the merge call.</summary>
    private abstract record SquashChoice
    {
        private SquashChoice()
        {
        }

        public sealed record Chosen(bool Squash) : SquashChoice;

        public sealed record Refused(MergeRequestOutcome Outcome) : SquashChoice;
    }

    private sealed record GitLabSquashSettingsDto(string? SquashOption);
}
