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
    private const string OpenedState = "opened";
    private const string LockedState = "locked";
    private const string MergedState = "merged";
    private const string ClosedState = "closed";

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
            .Where(dto => dto.ProjectId == projectId)
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
            $"projects/{encodedPath}/issues?state=opened&confidential=false&order_by=created_at&sort=desc&per_page={OpenIssuesPerPage}";
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

    /// <summary>
    /// Reads one same-project issue (<c>GET /projects/:id/issues/:issue_iid</c>) and the merge requests that close
    /// it on merge (<c>GET /projects/:id/issues/:issue_iid/closed_by</c>). It never reads
    /// <c>related_merge_requests</c>, which lists merge requests that merely mention the issue.
    /// </summary>
    public async Task<Result<ProviderReferencedIssue>> GetReferencedIssueAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string issuePath = $"projects/{encodedPath}/issues/{issueNumber}";

        Result<GitLabReferencedIssueDto> issueResult = await ReadJsonAsync<GitLabReferencedIssueDto>(
            apiBaseUrl, issuePath, token, cancellationToken);

        if (issueResult is not Result<GitLabReferencedIssueDto>.Success issueSuccess)
        {
            return Result<ProviderReferencedIssue>.Fail(((Result<GitLabReferencedIssueDto>.Failure)issueResult).Error);
        }

        if (issueSuccess.Value.Confidential)
        {
            return Result<ProviderReferencedIssue>.Fail(TrackerReadErrors.Confidential());
        }

        Result<List<GitLabClosingMergeRequestDto>> closedByResult =
            await ReadJsonAsync<List<GitLabClosingMergeRequestDto>>(
                apiBaseUrl, $"{issuePath}/closed_by", token, cancellationToken);

        if (closedByResult is not Result<List<GitLabClosingMergeRequestDto>>.Success closedBySuccess)
        {
            return Result<ProviderReferencedIssue>.Fail(
                ((Result<List<GitLabClosingMergeRequestDto>>.Failure)closedByResult).Error);
        }

        GitLabReferencedIssueDto issue = issueSuccess.Value;

        return Result<ProviderReferencedIssue>.Ok(new ProviderReferencedIssue(
            issue.Title,
            string.Equals(issue.State, OpenedState, StringComparison.Ordinal),
            issue.Labels ?? [],
            StrongestClosingState(closedBySuccess.Value),
            issue.Description ?? string.Empty));
    }

    private async Task<Result<T>> ReadJsonAsync<T>(
        Uri apiBaseUrl,
        string relativePath,
        string token,
        CancellationToken cancellationToken)
        where T : class
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<T>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<T>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        T? value = JsonSerializer.Deserialize<T>(body, JsonOptions);

        return value is null
            ? Result<T>.Fail(GitLabErrors.ProviderError($"GitLab returned an empty body for {relativePath}."))
            : Result<T>.Ok(value);
    }

    private static ClosingMergeRequestState StrongestClosingState(
        IReadOnlyList<GitLabClosingMergeRequestDto> closingMergeRequests)
    {
        return closingMergeRequests
            .Select(mergeRequest => mergeRequest.State switch
            {
                MergedState => ClosingMergeRequestState.Merged,
                OpenedState or LockedState => ClosingMergeRequestState.Open,
                ClosedState => ClosingMergeRequestState.Closed,
                _ => ClosingMergeRequestState.None,
            })
            .DefaultIfEmpty(ClosingMergeRequestState.None)
            .MaxBy(ClosingStatePrecedence);
    }

    // Merged outranks open, which outranks closed, which outranks none.
    private static int ClosingStatePrecedence(ClosingMergeRequestState state) => state switch
    {
        ClosingMergeRequestState.Merged => 3,
        ClosingMergeRequestState.Open => 2,
        ClosingMergeRequestState.Closed => 1,
        _ => 0,
    };

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

    private sealed record GitLabReferencedIssueDto(
        string Title,
        string? Description,
        string State,
        IReadOnlyList<string>? Labels,
        bool Confidential = false);

    private sealed record GitLabClosingMergeRequestDto(string State);
}
