using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    /// <summary>
    /// Creates or updates the project webhook: lists the project's hooks, updates the one whose
    /// <c>url</c> matches the payload URL (or a previous one) in place with <c>PUT</c> — URL, secret
    /// token and trigger flags — and otherwise creates a new hook with <c>POST</c>. Every trigger is
    /// sent explicitly, so the hook subscribes to exactly
    /// <see cref="GitLabWebhookEventMapper.SubscribedTriggers"/> (GitLab otherwise defaults push events on).
    /// A <c>403</c> means the token's user is below the Maintainer role.
    /// </summary>
    public async Task<Result<WebhookUpsertKind>> UpsertWebhookAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        WebhookHookSpec spec,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<WebhookUpsertKind>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri baseUri = EnsureTrailingSlash(apiBaseUrl);
        Uri hooksUri = new(baseUri, $"projects/{encodedPath}/hooks");

        using HttpRequestMessage listRequest = new(HttpMethod.Get, new Uri(baseUri, $"projects/{encodedPath}/hooks?per_page=100"));
        AddCommonHeaders(listRequest, token);

        using HttpResponseMessage listResponse = await httpClient.SendAsync(listRequest, cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            return Result<WebhookUpsertKind>.Fail(
                await ErrorFromWebhookFailureAsync(listResponse, slug, cancellationToken));
        }

        RecordRestHeadroom(listResponse);

        string listBody = await listResponse.Content.ReadAsStringAsync(cancellationToken);
        List<GitLabHookDto> hooks = JsonSerializer.Deserialize<List<GitLabHookDto>>(listBody, JsonOptions) ?? [];

        GitLabHookDto? existing =
            hooks.FirstOrDefault(h => string.Equals(h.Url, spec.PayloadUrl, StringComparison.Ordinal))
            ?? hooks.FirstOrDefault(h => spec.Matches(h.Url));

        (HttpMethod method, Uri writeUri, WebhookUpsertKind kind) = existing is null
            ? (HttpMethod.Post, hooksUri, WebhookUpsertKind.Created)
            : (HttpMethod.Put, new Uri(baseUri, $"projects/{encodedPath}/hooks/{existing.Id}"), WebhookUpsertKind.Updated);

        using HttpRequestMessage writeRequest = new(method, writeUri);
        AddCommonHeaders(writeRequest, token);
        writeRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildHookPayload(spec)),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage writeResponse = await httpClient.SendAsync(writeRequest, cancellationToken);

        if (!writeResponse.IsSuccessStatusCode)
        {
            return Result<WebhookUpsertKind>.Fail(
                await ErrorFromWebhookFailureAsync(writeResponse, slug, cancellationToken));
        }

        RecordRestHeadroom(writeResponse);
        return Result<WebhookUpsertKind>.Ok(kind);
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
            ["enable_ssl_verification"] = true,
        };

        foreach (string trigger in GitLabWebhookEventMapper.AllTriggers)
        {
            payload[trigger] = GitLabWebhookEventMapper.SubscribedTriggers.Contains(trigger, StringComparer.Ordinal);
        }

        return payload;
    }

    private static async Task<Error> ErrorFromWebhookFailureAsync(
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
            return WebhookWriteErrors.InsufficientPermission(
                $"Registering the webhook on {slug} returned 403 — the branch-rules token needs the " +
                "Maintainer role and the api scope." +
                (providerMessage is null ? string.Empty : $" GitLab: {providerMessage}"));
        }

        int statusCode = (int)response.StatusCode;
        return GitLabErrors.ProviderError(
            $"Registering the webhook on {slug} returned {statusCode}" +
            (providerMessage is null ? "." : $" — {providerMessage}"));
    }

    private sealed record GitLabHookDto(long Id, string? Url);
}
