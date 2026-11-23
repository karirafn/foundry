using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

/// <summary>
/// Marks a draft merge request ready. GitLab's update endpoint has no draft attribute: a merge request is a
/// draft when its title starts with <c>[Draft]</c>, <c>Draft:</c> or <c>(Draft)</c>
/// (https://docs.gitlab.com/user/project/merge_requests/drafts/), so the prefix is removed from the title.
/// </summary>
internal sealed partial class GitLabHttpClient
{
    /// <summary>
    /// The prefix every merge request Foundry opens carries, so it opens as a draft; Foundry marks it ready once its
    /// review cycle ends.
    /// </summary>
    private const string DraftTitlePrefix = "Draft: ";

    /// <summary>
    /// Reads the merge request's current title (<c>GET /projects/:id/merge_requests/:iid</c>) and, when it starts
    /// with a draft prefix, <c>PUT</c>s the title with that one prefix and the whitespace after it removed. The
    /// rest of the title is sent back as read, so a person's edits survive. A title without a prefix sends no
    /// <c>PUT</c> and succeeds.
    /// </summary>
    public async Task<Result> MarkMergeRequestReadyAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string mergeRequestUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result.Fail(RunPublishingErrors.Permanent(GitLabErrors.InvalidBaseUrl.Message));
        }

        if (!TryParseMrIid(mergeRequestUrl, out int mrIid))
        {
            return Result.Fail(RunPublishingErrors.Permanent(GitLabErrors.InvalidMergeRequestUrl.Message));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri mergeRequestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/merge_requests/{mrIid}");
        string action = $"Marking merge request !{mrIid} on {slug} ready";

        Result<string> title = await ReadMergeRequestTitleAsync(mergeRequestUri, action, token, cancellationToken);
        if (title is not Result<string>.Success titleSuccess)
        {
            return Result.Fail(((Result<string>.Failure)title).Error);
        }

        Match prefix = DraftTitlePrefixRegex().Match(titleSuccess.Value);
        if (!prefix.Success)
        {
            return Result.Ok();
        }

        string readyTitle = titleSuccess.Value[prefix.Length..];

        using HttpRequestMessage request = new(HttpMethod.Put, mergeRequestUri);
        AddCommonHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new GitLabMergeRequestTitleDto(readyTitle), JsonOptions),
            Encoding.UTF8,
            "application/json");

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);
            return Result.Ok();
        }
    }

    private async Task<Result<string>> ReadMergeRequestTitleAsync(
        Uri mergeRequestUri,
        string action,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, mergeRequestUri);
        AddCommonHeaders(request, token);

        Result<HttpResponseMessage> sent = await SendPublishingRequestAsync(request, action, cancellationToken);
        if (sent is not Result<HttpResponseMessage>.Success { Value: { } response })
        {
            return Result<string>.Fail(((Result<HttpResponseMessage>.Failure)sent).Error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<string>.Fail(ClassifyPublishingFailure(response, action));
            }

            RecordRestHeadroom(response);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            GitLabMergeRequestTitleDto? mergeRequest =
                JsonSerializer.Deserialize<GitLabMergeRequestTitleDto>(responseBody, JsonOptions);

            return mergeRequest?.Title is { } title
                ? Result<string>.Ok(title)
                : Result<string>.Fail(RunPublishingErrors.Permanent($"{action}: GitLab returned no merge request title."));
        }
    }

    [GeneratedRegex(@"\A(?:Draft:|\[Draft\]|\(Draft\))\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DraftTitlePrefixRegex();

    private sealed record GitLabMergeRequestTitleDto(string? Title);
}
