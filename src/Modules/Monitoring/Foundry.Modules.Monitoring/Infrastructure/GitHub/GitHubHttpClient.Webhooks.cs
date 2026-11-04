using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    private const int MaxWebhookErrorBodyLength = 500;

    private static readonly JsonSerializerOptions WebhookJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Creates or updates the repository webhook: lists the repository's hooks, updates the one whose
    /// <c>config.url</c> matches the payload URL (or a previous one) in place with <c>PATCH</c> —
    /// URL, secret, JSON content type, events and active flag — and otherwise creates a new hook with
    /// <c>POST</c>. A <c>403</c> or <c>404</c> on <c>/hooks</c> means the token lacks the
    /// <b>Webhooks</b> permission (GitHub answers 404 for resources a token cannot see).
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
            return Result<WebhookUpsertKind>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri baseUri = EnsureTrailingSlash(apiBaseUrl);
        Uri hooksUri = new(baseUri, $"repos/{owner}/{repo}/hooks");

        using HttpRequestMessage listRequest = CreateRulesetRequest(
            HttpMethod.Get,
            new Uri(baseUri, $"repos/{owner}/{repo}/hooks?per_page=100"),
            token);
        using HttpResponseMessage listResponse = await httpClient.SendAsync(listRequest, cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            return Result<WebhookUpsertKind>.Fail(
                await ErrorFromWebhookFailureAsync(listResponse, slug, cancellationToken));
        }

        RecordRestHeadroom(listResponse);

        string listBody = await listResponse.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubHookDto> hooks = JsonSerializer.Deserialize<List<GitHubHookDto>>(listBody, JsonOptions) ?? [];

        GitHubHookDto? existing = FindHook(hooks, spec);

        GitHubHookWriteDto payload = BuildHookPayload(spec, includeName: existing is null);

        (HttpMethod method, Uri writeUri, WebhookUpsertKind kind) = existing is null
            ? (HttpMethod.Post, hooksUri, WebhookUpsertKind.Created)
            : (HttpMethod.Patch, new Uri(baseUri, $"repos/{owner}/{repo}/hooks/{existing.Id}"), WebhookUpsertKind.Updated);

        using HttpRequestMessage writeRequest = CreateRulesetRequest(method, writeUri, token);
        writeRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, WebhookJsonOptions),
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
    /// The hook to update: one at the current payload URL first, else one at a previous payload URL
    /// (a changed Webhook Public Base URL), else <see langword="null"/>.
    /// </summary>
    private static GitHubHookDto? FindHook(IReadOnlyList<GitHubHookDto> hooks, WebhookHookSpec spec) =>
        hooks.FirstOrDefault(h => string.Equals(h.Config?.Url, spec.PayloadUrl, StringComparison.Ordinal))
        ?? hooks.FirstOrDefault(h => spec.Matches(h.Config?.Url));

    internal static GitHubHookWriteDto BuildHookPayload(WebhookHookSpec spec, bool includeName) =>
        new(
            Name: includeName ? "web" : null,
            Active: true,
            Events: [.. GitHubWebhookEventMapper.SubscribedEvents],
            Config: new GitHubHookConfigWriteDto(
                Url: spec.PayloadUrl,
                ContentType: "json",
                Secret: spec.Secret,
                InsecureSsl: "0"));

    private static async Task<Error> ErrorFromWebhookFailureAsync(
        HttpResponseMessage response,
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response))
        {
            return GitHubErrors.RateLimitExhausted;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubErrorBodyDto? errorDto = DeserializeErrorBody(body);
        string? providerMessage = string.IsNullOrWhiteSpace(errorDto?.Message)
            ? null
            : TruncateWithEllipsis(RedactSecrets(errorDto.Message), MaxWebhookErrorBodyLength);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return WebhookWriteErrors.InsufficientPermission(
                $"Registering the webhook on {slug} returned {(int)response.StatusCode} — the branch-rules " +
                "token needs the Webhooks (read and write) permission on the repository." +
                (providerMessage is null ? string.Empty : $" GitHub: {providerMessage}"));
        }

        int statusCode = (int)response.StatusCode;
        return GitHubErrors.ProviderError(
            $"Registering the webhook on {slug} returned {statusCode}" +
            (providerMessage is null ? "." : $" — {providerMessage}"));
    }

    private sealed record GitHubHookDto(long Id, GitHubHookConfigDto? Config);

    private sealed record GitHubHookConfigDto(string? Url);

    internal sealed record GitHubHookWriteDto(
        string? Name,
        bool Active,
        IReadOnlyList<string> Events,
        GitHubHookConfigWriteDto Config);

    internal sealed record GitHubHookConfigWriteDto(
        string Url,
        string ContentType,
        string Secret,
        string InsecureSsl);
}
