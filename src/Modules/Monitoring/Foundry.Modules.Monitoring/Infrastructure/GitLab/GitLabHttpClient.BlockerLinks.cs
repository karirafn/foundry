using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    private const string MessageProperty = "message";
    private const int MinServerErrorStatus = 500;

    /// <summary>
    /// Records that <paramref name="blockerNumber"/> blocks <paramref name="issueNumber"/>
    /// (<c>POST /projects/:id/issues/:issue_iid/links</c>, <c>link_type=is_blocked_by</c>) and reads the links
    /// back, because GitLab Free can answer 2xx without recording a blocking link.
    /// </summary>
    public async Task<Result> AddBlockerAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        int blockerNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result.Fail(BlockerLinkErrors.Permanent(GitLabErrors.InvalidBaseUrl.Message));
        }

        try
        {
            return await LinkBlockerAsync(apiBaseUrl, slug, issueNumber, blockerNumber, token, cancellationToken);
        }
        catch (Exception ex) when (BlockerLinkErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result.Fail(BlockerLinkErrors.Transient(
                $"Linking #{blockerNumber} as blocking #{issueNumber} did not complete: {ex.GetType().Name}."));
        }
    }

    private async Task<Result> LinkBlockerAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        int blockerNumber,
        string token,
        CancellationToken cancellationToken)
    {
        Result<int> projectIdResult = await ResolveProjectIdAsync(apiBaseUrl, slug, token, cancellationToken);

        if (projectIdResult is not Result<int>.Success projectIdSuccess)
        {
            return Result.Fail(ClassifyReadFailure(
                ((Result<int>.Failure)projectIdResult).Error, $"Looking up the project of {slug}"));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/issues/{issueNumber}/links");

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddCommonHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(
                new GitLabIssueLinkWriteDto(projectIdSuccess.Value, blockerNumber, BlockedByLinkType),
                JsonOptions),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        RecordRestHeadroom(response);

        if (IsRetryableStatus(response))
        {
            return Result.Fail(BlockerLinkErrors.Transient(
                $"Linking #{blockerNumber} as blocking #{issueNumber} returned {(int)response.StatusCode}."));
        }

        Error notRecorded = BlockerLinkErrors.Permanent(
            $"GitLab did not record #{blockerNumber} as blocking #{issueNumber}; " +
            "blocking links need GitLab Premium or Ultimate");

        if (response.IsSuccessStatusCode)
        {
            return await ConfirmBlockerAsync(
                apiBaseUrl, slug, projectIdSuccess.Value, issueNumber, blockerNumber, notRecorded, token, cancellationToken);
        }

        string detail = await ReadRefusalAsync(response, cancellationToken);
        Error refused = BlockerLinkErrors.Permanent(
            $"Linking #{blockerNumber} as blocking #{issueNumber} returned {(int)response.StatusCode}: {detail}");

        // GitLab also answers 409 when the issues are already linked with another type, so only the read-back
        // shows whether the blocking link exists.
        return response.StatusCode is HttpStatusCode.Conflict
            ? await ConfirmBlockerAsync(
                apiBaseUrl, slug, projectIdSuccess.Value, issueNumber, blockerNumber, refused, token, cancellationToken)
            : Result.Fail(refused);
    }

    private static async Task<string> ReadRefusalAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || !document.RootElement.TryGetProperty(MessageProperty, out JsonElement message))
            {
                return string.Empty;
            }

            // GitLab sends message as a string, an array of strings, or an object keyed by attribute.
            string text = message.ValueKind is JsonValueKind.String ? message.GetString() ?? string.Empty : message.GetRawText();

            return SingleLineText.Cap(text);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private async Task<Result> ConfirmBlockerAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int projectId,
        int issueNumber,
        int blockerNumber,
        Error notRecorded,
        string token,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<IssueReference>> linksResult =
            await ReadIssueLinksAsync(apiBaseUrl, slug, projectId, issueNumber, token, cancellationToken);

        if (linksResult is not Result<IReadOnlyList<IssueReference>>.Success links)
        {
            return Result.Fail(ClassifyReadFailure(
                ((Result<IReadOnlyList<IssueReference>>.Failure)linksResult).Error,
                $"Reading back the links of #{issueNumber}"));
        }

        return links.Value.Contains(new IssueReference(blockerNumber, IssueRelation.BlockedBy))
            ? Result.Ok()
            : Result.Fail(notRecorded);
    }

    // Classifies a failed GET like the link POST: 429, 5xx and a rate-limited 403 retry; any other status will
    // fail the same way again.
    private static Error ClassifyReadFailure(Error failure, string action)
    {
        string message = $"{action} failed: {failure.Message}";
        bool retryable = string.Equals(failure.Code, GitLabErrors.RateLimitExhausted.Code, StringComparison.Ordinal)
            || (GitLabErrors.TryGetStatusCode(failure, out int status)
                && (status >= MinServerErrorStatus || status is (int)HttpStatusCode.RequestTimeout));

        return retryable ? BlockerLinkErrors.Transient(message) : BlockerLinkErrors.Permanent(message);
    }

    private sealed record GitLabIssueLinkWriteDto(int TargetProjectId, int TargetIssueIid, string LinkType);
}
