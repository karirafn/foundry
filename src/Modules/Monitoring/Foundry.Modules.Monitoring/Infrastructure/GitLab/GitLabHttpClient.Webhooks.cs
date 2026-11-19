using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    /// <summary>
    /// Creates or updates the project webhook: lists the project's hooks and picks those whose
    /// <c>url</c> carries the repository's hook id (<see cref="WebhookHookSpec.Matches"/>). With none, a
    /// new hook is created with <c>POST</c>; otherwise one is updated in place with <c>PUT</c> — URL,
    /// secret token and trigger flags — preferring an exact payload-URL match, then the first listed, and
    /// every other match is deleted. Every trigger is sent explicitly, so the hook subscribes to exactly
    /// <see cref="GitLabWebhookEventMapper.SubscribedTriggers"/> (GitLab otherwise defaults push events on).
    /// A <c>403</c> means the token's user is below the Maintainer role.
    /// </summary>
    /// <remarks>
    /// A failed delete of a duplicate is logged and does not fail the upsert: the duplicate only
    /// receives deliveries the endpoint already verifies and coalesces.
    /// </remarks>
    public async Task<Result<WebhookUpsert>> UpsertWebhookAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        WebhookHookSpec spec,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WebhookUpsert>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri baseUri = EnsureTrailingSlash(apiBaseUrl);
        Uri hooksUri = new(baseUri, $"projects/{encodedPath}/hooks");

        Result<List<GitLabHookDto>> listed = await ListHooksAsync(baseUri, encodedPath, slug, token, RegisteringAction, cancellationToken);
        if (listed is not Result<List<GitLabHookDto>>.Success { Value: { } hooks })
        {
            return Result<WebhookUpsert>.Fail(((Result<List<GitLabHookDto>>.Failure)listed).Error);
        }

        List<GitLabHookDto> matches = [.. hooks.Where(h => spec.Matches(h.Url))];
        GitLabHookDto? existing =
            matches.FirstOrDefault(h => string.Equals(h.Url, spec.PayloadUrl, StringComparison.Ordinal))
            ?? matches.FirstOrDefault();

        (HttpMethod method, Uri writeUri, WebhookUpsertKind kind) = existing is null
            ? (HttpMethod.Post, hooksUri, WebhookUpsertKind.Created)
            : (HttpMethod.Put, HookUri(baseUri, encodedPath, existing.Id), WebhookUpsertKind.Updated);

        using HttpRequestMessage writeRequest = new(method, writeUri);
        AddCommonHeaders(writeRequest, token);
        writeRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildHookPayload(spec)),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage writeResponse = await httpClient.SendAsync(writeRequest, cancellationToken);

        if (!writeResponse.IsSuccessStatusCode)
        {
            return Result<WebhookUpsert>.Fail(
                await ErrorFromWebhookFailureAsync(writeResponse, slug, RegisteringAction, cancellationToken));
        }

        RecordRestHeadroom(writeResponse);

        if ((existing?.Id ?? await ReadCreatedHookIdAsync(writeResponse, cancellationToken)) is not { } providerHookId)
        {
            return Result<WebhookUpsert>.Fail(
                GitLabErrors.ProviderError($"{RegisteringAction} on {slug} returned no hook id."));
        }

        foreach (GitLabHookDto duplicate in matches.Where(h => h.Id != existing?.Id))
        {
            Result deleted = await DeleteHookAsync(baseUri, encodedPath, duplicate.Id, slug, token, cancellationToken);
            if (deleted is Result.Failure failure)
            {
                logger.LogWarning(
                    "Deleting duplicate webhook {HookId} on {Slug} failed ({ErrorCode}); it stays on the provider.",
                    duplicate.Id,
                    slug,
                    failure.Error.Code);
            }
        }

        return Result<WebhookUpsert>.Ok(new WebhookUpsert(kind, providerHookId));
    }

    /// <summary>
    /// Deletes every project hook whose <c>url</c> carries the repository's hook id
    /// (<paramref name="hookPath"/>) and returns how many were deleted. Stops at the first failure.
    /// </summary>
    public async Task<Result<int>> DeleteWebhooksAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        WebhookHookPath hookPath,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<int>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri baseUri = EnsureTrailingSlash(apiBaseUrl);

        Result<List<GitLabHookDto>> listed = await ListHooksAsync(baseUri, encodedPath, slug, token, DeletingAction, cancellationToken);
        if (listed is not Result<List<GitLabHookDto>>.Success { Value: { } hooks })
        {
            return Result<int>.Fail(((Result<List<GitLabHookDto>>.Failure)listed).Error);
        }

        int deletedCount = 0;
        foreach (GitLabHookDto hook in hooks.Where(h => hookPath.Matches(h.Url)))
        {
            Result deleted = await DeleteHookAsync(baseUri, encodedPath, hook.Id, slug, token, cancellationToken);
            if (deleted is Result.Failure failure)
            {
                return Result<int>.Fail(failure.Error);
            }

            deletedCount++;
        }

        return Result<int>.Ok(deletedCount);
    }

    /// <summary>
    /// Asks GitLab to send a test push delivery to the hook
    /// (<c>POST /projects/:id/hooks/:hook_id/test/push_events</c>, 201 on success). GitLab allows five
    /// test requests per minute per project and user; a 429 fails like any other status.
    /// </summary>
    public async Task<Result> TestWebhookAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        long providerHookId,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri baseUri = EnsureTrailingSlash(apiBaseUrl);
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri(baseUri, $"projects/{encodedPath}/hooks/{providerHookId}/test/push_events"));
        AddCommonHeaders(request, token);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail(await ErrorFromWebhookFailureAsync(response, slug, TestingAction, cancellationToken));
        }

        RecordRestHeadroom(response);
        return Result.Ok();
    }

    private const string RegisteringAction = "Registering the webhook";

    private const string DeletingAction = "Deleting the webhooks";

    private const string TestingAction = "Sending a test delivery to the webhook";

    private static async Task<long?> ReadCreatedHookIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<GitLabHookDto>(body, JsonOptions) is { Id: > 0 } created ? created.Id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Uri HookUri(Uri baseUri, string encodedPath, long hookId) =>
        new(baseUri, $"projects/{encodedPath}/hooks/{hookId}");

    private async Task<Result<List<GitLabHookDto>>> ListHooksAsync(
        Uri baseUri,
        string encodedPath,
        RepositorySlug slug,
        string token,
        string action,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage listRequest = new(HttpMethod.Get, new Uri(baseUri, $"projects/{encodedPath}/hooks?per_page=100"));
        AddCommonHeaders(listRequest, token);

        using HttpResponseMessage listResponse = await httpClient.SendAsync(listRequest, cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            return Result<List<GitLabHookDto>>.Fail(
                await ErrorFromWebhookFailureAsync(listResponse, slug, action, cancellationToken));
        }

        RecordRestHeadroom(listResponse);

        string listBody = await listResponse.Content.ReadAsStringAsync(cancellationToken);
        return Result<List<GitLabHookDto>>.Ok(
            JsonSerializer.Deserialize<List<GitLabHookDto>>(listBody, JsonOptions) ?? []);
    }

    private async Task<Result> DeleteHookAsync(
        Uri baseUri,
        string encodedPath,
        long hookId,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, HookUri(baseUri, encodedPath, hookId));
        AddCommonHeaders(request, token);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail(await ErrorFromWebhookFailureAsync(response, slug, DeletingAction, cancellationToken));
        }

        RecordRestHeadroom(response);
        return Result.Ok();
    }

    /// <summary>
    /// The hook body: URL, secret token, TLS verification on, and every trigger flag GitLab offers set
    /// explicitly — <see langword="true"/> for the mapped triggers, <see langword="false"/> for the rest.
    /// </summary>
    internal static Dictionary<string, object> BuildHookPayload(WebhookHookSpec spec)
    {
        Dictionary<string, object> payload = new(StringComparer.Ordinal)
        {
            ["url"] = spec.PayloadUrl,
            ["token"] = spec.Secret,
            ["enable_ssl_verification"] = WebhookHookSettings.GitLabEnableSslVerification,
        };

        foreach (KeyValuePair<string, bool> flag in WebhookHookSettings.GitLabTriggerFlags())
        {
            payload[flag.Key] = flag.Value;
        }

        return payload;
    }

    private static async Task<Error> ErrorFromWebhookFailureAsync(
        HttpResponseMessage response,
        RepositorySlug slug,
        string action,
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
            return WebhookWriteErrors.InsufficientPermission(
                $"{action} on {slug} returned 403 — the account token needs the " +
                "Maintainer role and the api scope." +
                (providerMessage is null ? string.Empty : $" GitLab: {providerMessage}"));
        }

        int statusCode = (int)response.StatusCode;
        return GitLabErrors.ProviderError(
            $"{action} on {slug} returned {statusCode}" +
            (providerMessage is null ? "." : $" — {providerMessage}"));
    }

    private sealed record GitLabHookDto(long Id, string? Url);
}
