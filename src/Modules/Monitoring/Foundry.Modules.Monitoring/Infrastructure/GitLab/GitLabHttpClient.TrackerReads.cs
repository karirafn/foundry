using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    private const string BlockedByLinkType = "is_blocked_by";
    private const string BlocksLinkType = "blocks";
    private const string RelatesToLinkType = "relates_to";
    private const int OpenIssuesPerPage = 100;

    /// <summary>
    /// Reads the links of <paramref name="issueNumber"/> (<c>GET /projects/:id/issues/:issue_iid/links</c>),
    /// of any state. GitLab reports each <c>link_type</c> from the requested issue's side.
    /// </summary>
    public async Task<Result<IReadOnlyList<IssueReference>>> GetIssueLinksAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<IssueReference>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        Result<int> projectIdResult = await ResolveProjectIdAsync(apiBaseUrl, slug, token, cancellationToken);

        if (projectIdResult is not Result<int>.Success projectIdSuccess)
        {
            return Result<IReadOnlyList<IssueReference>>.Fail(((Result<int>.Failure)projectIdResult).Error);
        }

        int projectId = projectIdSuccess.Value;

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath = $"projects/{encodedPath}/issues/{issueNumber}/links";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            RecordRestHeadroom(response);
            return Result<IReadOnlyList<IssueReference>>.Ok([]);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<IssueReference>>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabIssueLinkDto> dtos = JsonSerializer.Deserialize<List<GitLabIssueLinkDto>>(body, JsonOptions) ?? [];

        IReadOnlyList<IssueReference> references = dtos
            .Where(dto => dto.ProjectId is null || dto.ProjectId == projectId)
            .Select(dto => (dto.Iid, Relation: RelationOf(dto.LinkType)))
            .Where(link => link.Relation is not null)
            .Select(link => new IssueReference(link.Iid, link.Relation!.Value))
            .ToList();

        return Result<IReadOnlyList<IssueReference>>.Ok(references);
    }

    /// <summary>
    /// Reads the project's open issues, newest first, at most one page of 100, without the label filter
    /// <see cref="GetIssuesAsync"/> applies (<c>GET /projects/:id/issues</c>).
    /// </summary>
    public async Task<Result<IReadOnlyList<OpenIssue>>> GetOpenIssuesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<OpenIssue>>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string relativePath =
            $"projects/{encodedPath}/issues?state=opened&order_by=created_at&sort=desc&per_page={OpenIssuesPerPage}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<OpenIssue>>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabOpenIssueDto> dtos = JsonSerializer.Deserialize<List<GitLabOpenIssueDto>>(body, JsonOptions) ?? [];

        IReadOnlyList<OpenIssue> issues = dtos
            .Select(dto => new OpenIssue(dto.Iid, dto.Title))
            .ToList();

        return Result<IReadOnlyList<OpenIssue>>.Ok(issues);
    }

    private static IssueRelation? RelationOf(string linkType)
    {
        return linkType switch
        {
            BlockedByLinkType => IssueRelation.BlockedBy,
            BlocksLinkType => IssueRelation.Blocks,
            RelatesToLinkType => IssueRelation.RelatesTo,
            _ => null,
        };
    }

    private sealed record GitLabOpenIssueDto(int Iid, string Title);
}
