using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    private const int MaxProviderMessageLength = 500;

    /// <summary>
    /// Records that <paramref name="issueNumber"/> is blocked by <paramref name="blockerNumber"/>: reads the
    /// blocker's id (<c>GET /repos/{owner}/{repo}/issues/{blocker}</c>), then posts it to
    /// <c>POST /repos/{owner}/{repo}/issues/{issue_number}/dependencies/blocked_by</c>. A <c>422</c> counts as
    /// success only when a read-back of the issue's links shows the blocker, because GitHub documents no
    /// duplicate-link response.
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
            return BlockerLinkErrors.Permanent(GitHubErrors.InvalidBaseUrl.Message);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri baseUrl = EnsureTrailingSlash(apiBaseUrl);

        Result<long> blockerId = await ReadIssueIdAsync(
            baseUrl, $"repos/{owner}/{repo}/issues/{blockerNumber}", slug, blockerNumber, token, cancellationToken);

        if (blockerId is not Result<long>.Success idSuccess)
        {
            return ((Result<long>.Failure)blockerId).Error;
        }

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri(baseUrl, $"repos/{owner}/{repo}/issues/{issueNumber}/dependencies/blocked_by"));
        AddGitHubHeaders(request, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new GitHubBlockedByWriteDto(idSuccess.Value), JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return BlockerLinkErrors.Transient(
                $"Linking blocker #{blockerNumber} to {slug}#{issueNumber} did not complete: {ex.GetType().Name}.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return Result.Ok();
            }

            if (response.StatusCode is HttpStatusCode.UnprocessableEntity)
            {
                string rejection = await response.Content.ReadAsStringAsync(cancellationToken);
                return await ConfirmByReadBackAsync(
                    apiBaseUrl, slug, issueNumber, blockerNumber, token, DescribeRejection(rejection), cancellationToken);
            }

            return ClassifyBlockerLinkFailure(response, $"Linking blocker #{blockerNumber} to {slug}#{issueNumber}");
        }
    }

    private async Task<Result<long>> ReadIssueIdAsync(
        Uri baseUrl,
        string relativePath,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUrl, relativePath));
        AddGitHubHeaders(request, token);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result<long>.Fail(BlockerLinkErrors.Transient(
                $"Reading {slug}#{issueNumber} did not complete: {ex.GetType().Name}."));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Result<long>.Fail(ClassifyBlockerLinkFailure(response, $"Reading {slug}#{issueNumber}"));
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Result<long>.Ok(JsonSerializer.Deserialize<GitHubBlockerIdDto>(body, JsonOptions)!.Id);
        }
    }

    private async Task<Result> ConfirmByReadBackAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        int issueNumber,
        int blockerNumber,
        string token,
        string rejection,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<IssueReference>> links;
        try
        {
            links = await GetIssueLinksAsync(apiBaseUrl, slug, issueNumber, token, cancellationToken);
        }
        catch (Exception ex) when (CommentPostErrors.IsTransportFailure(ex, cancellationToken))
        {
            return BlockerLinkErrors.Transient(
                $"Reading the links of {slug}#{issueNumber} to confirm the blocker did not complete: {ex.GetType().Name}.");
        }

        if (links is not Result<IReadOnlyList<IssueReference>>.Success success)
        {
            return BlockerLinkErrors.Transient(
                $"Reading the links of {slug}#{issueNumber} to confirm the blocker did not complete.");
        }

        bool recorded = success.Value.Any(
            link => link.Number == blockerNumber && link.Relation is IssueRelation.BlockedBy);

        return recorded ? Result.Ok() : BlockerLinkErrors.Permanent(rejection);
    }

    private static Error ClassifyBlockerLinkFailure(HttpResponseMessage response, string action)
    {
        string message = $"{action} returned {(int)response.StatusCode}.";
        return IsRetryableStatus(response) ? BlockerLinkErrors.Transient(message) : BlockerLinkErrors.Permanent(message);
    }

    private static string DescribeRejection(string responseBody)
    {
        GitHubErrorEnvelopeDto? envelope = JsonSerializer.Deserialize<GitHubErrorEnvelopeDto>(responseBody, JsonOptions);
        string? detail = envelope?.Errors?.Select(error => error.Message).FirstOrDefault(message => message is not null);
        string message = detail is null ? envelope?.Message ?? string.Empty : $"{envelope?.Message}: {detail}";
        string flattened = message.Trim().ReplaceLineEndings(" ");

        return flattened.Length <= MaxProviderMessageLength ? flattened : flattened[..MaxProviderMessageLength];
    }

    private sealed record GitHubBlockerIdDto(long Id);

    private sealed record GitHubBlockedByWriteDto(long IssueId);

    private sealed record GitHubErrorEnvelopeDto(string? Message, List<GitHubErrorDetailDto>? Errors);

    private sealed record GitHubErrorDetailDto(string? Message);
}
