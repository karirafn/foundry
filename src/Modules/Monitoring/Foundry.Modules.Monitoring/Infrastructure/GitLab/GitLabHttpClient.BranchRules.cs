using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    private const int NoAccessLevel = 0;
    private const int MaintainerAccessLevel = 40;
    private const int MaxProtectionErrorBodyLength = 500;

    /// <summary>
    /// Makes <paramref name="branch"/> reject direct and force pushes without ever unprotecting it
    /// (unprotect-then-reprotect would fail open). An already-protected branch is <c>PATCH</c>ed in
    /// place: force pushes are disabled, every push grant other than "No one" is destroyed and a
    /// "No one" (<c>access_level: 0</c>) grant is added when absent. Merge and unprotect settings are
    /// left untouched. An unprotected branch is protected with <c>POST</c> (push: No one,
    /// merge: Maintainers, no force push).
    /// </summary>
    public async Task<Result> ApplyBranchRulesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string branch,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string encodedBranch = Uri.EscapeDataString(branch);
        Uri baseUri = EnsureTrailingSlash(apiBaseUrl);
        Uri branchUri = new(baseUri, $"projects/{encodedPath}/protected_branches/{encodedBranch}");

        using HttpRequestMessage getRequest = new(HttpMethod.Get, branchUri);
        AddCommonHeaders(getRequest, token);

        using HttpResponseMessage getResponse = await httpClient.SendAsync(getRequest, cancellationToken);

        if (getResponse.StatusCode == HttpStatusCode.NotFound)
        {
            RecordRestHeadroom(getResponse);

            object protectBody = new
            {
                name = branch,
                push_access_level = NoAccessLevel,
                merge_access_level = MaintainerAccessLevel,
                allow_force_push = false,
            };

            Uri protectUri = new(baseUri, $"projects/{encodedPath}/protected_branches");
            return await SendProtectionWriteAsync(HttpMethod.Post, protectUri, protectBody, slug, token, cancellationToken);
        }

        if (!getResponse.IsSuccessStatusCode)
        {
            return Result.Fail(await ErrorFromProtectionFailureAsync(getResponse, slug, cancellationToken));
        }

        RecordRestHeadroom(getResponse);

        string body = await getResponse.Content.ReadAsStringAsync(cancellationToken);
        GitLabProtectedBranchWithIdsDto? existing =
            JsonSerializer.Deserialize<GitLabProtectedBranchWithIdsDto>(body, JsonOptions);

        object patchBody = new
        {
            allow_force_push = false,
            allowed_to_push = BuildAllowedToPush(existing?.PushAccessLevels ?? []),
        };

        return await SendProtectionWriteAsync(HttpMethod.Patch, branchUri, patchBody, slug, token, cancellationToken);
    }

    /// <summary>
    /// Builds the <c>allowed_to_push</c> array for the protected-branch <c>PATCH</c>: destroys every
    /// existing grant (<c>{ id, _destroy: true }</c>) and adds <c>{ access_level: 0 }</c>. A grant
    /// that is already exactly "No one" is kept instead of destroyed and re-added, because GitLab
    /// validates access-level uniqueness against the persisted rows within the same request.
    /// </summary>
    internal static IReadOnlyList<Dictionary<string, object>> BuildAllowedToPush(
        IReadOnlyList<GitLabPushAccessLevelDto> existing)
    {
        List<Dictionary<string, object>> entries = [];
        bool hasNoOneGrant = false;

        foreach (GitLabPushAccessLevelDto level in existing)
        {
            if (IsNoOneGrant(level) && !hasNoOneGrant)
            {
                hasNoOneGrant = true;
                continue;
            }

            entries.Add(new Dictionary<string, object> { ["id"] = level.Id, ["_destroy"] = true });
        }

        if (!hasNoOneGrant)
        {
            entries.Add(new Dictionary<string, object> { ["access_level"] = NoAccessLevel });
        }

        return entries;
    }

    private static bool IsNoOneGrant(GitLabPushAccessLevelDto level) =>
        level.AccessLevel == NoAccessLevel
        && level.UserId is null
        && level.GroupId is null
        && level.DeployKeyId is null;

    private async Task<Result> SendProtectionWriteAsync(
        HttpMethod method,
        Uri uri,
        object payload,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, uri);
        AddCommonHeaders(request, token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail(await ErrorFromProtectionFailureAsync(response, slug, cancellationToken));
        }

        RecordRestHeadroom(response);
        return Result.Ok();
    }

    private static async Task<Error> ErrorFromProtectionFailureAsync(
        HttpResponseMessage response,
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests
            || (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response)))
        {
            return GitLabErrors.RateLimitExhausted;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? providerMessage = ReadErrorMessage(body);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return BranchRuleWriteErrors.InsufficientPermission(
                $"Writing branch protection on {slug} returned 403 — the branch-rules token needs " +
                $"the Maintainer role and the api scope." +
                (providerMessage is null ? string.Empty : $" GitLab: {providerMessage}"));
        }

        int statusCode = (int)response.StatusCode;
        return GitLabErrors.ProviderError(
            $"Writing branch protection on {slug} returned {statusCode}" +
            (providerMessage is null ? "." : $" — {providerMessage}"));
    }

    // GitLab error bodies carry "message" as either a string or an object of field errors.
    private static string? ReadErrorMessage(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("message", out JsonElement message))
            {
                return null;
            }

            string text = message.ValueKind == JsonValueKind.String
                ? message.GetString() ?? string.Empty
                : message.GetRawText();

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return text.Length <= MaxProtectionErrorBodyLength
                ? text
                : string.Concat(text.AsSpan(0, MaxProtectionErrorBodyLength), "...");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record GitLabProtectedBranchWithIdsDto(
        IReadOnlyList<GitLabPushAccessLevelDto>? PushAccessLevels);

    internal sealed record GitLabPushAccessLevelDto(
        long Id,
        int? AccessLevel,
        long? UserId,
        long? GroupId,
        long? DeployKeyId);
}
