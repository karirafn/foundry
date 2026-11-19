using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

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
    /// Creates or updates the repository webhook: lists the repository's hooks and picks those whose
    /// <c>config.url</c> carries the repository's hook id (<see cref="WebhookHookSpec.Matches"/>). With
    /// none, a new hook is created with <c>POST</c>; otherwise one is updated in place with <c>PATCH</c> —
    /// URL, secret, JSON content type, events and active flag — preferring an exact payload-URL match,
    /// then the first listed, and every other match is deleted. A <c>403</c> or <c>404</c> on
    /// <c>/hooks</c> means the token lacks the <b>Webhooks</b> permission (GitHub answers 404 for
    /// resources a token cannot see).
    /// </summary>
    /// <remarks>
    /// A failed delete of a duplicate is logged and does not fail the upsert: the duplicate only
    /// receives deliveries the endpoint already verifies and coalesces.
    /// </remarks>
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

        Result<List<GitHubHookDto>> listed = await ListHooksAsync(baseUri, owner, repo, slug, token, RegisteringAction, cancellationToken);
        if (listed is not Result<List<GitHubHookDto>>.Success { Value: { } hooks })
        {
            return Result<WebhookUpsertKind>.Fail(((Result<List<GitHubHookDto>>.Failure)listed).Error);
        }

        List<GitHubHookDto> matches = [.. hooks.Where(h => spec.Matches(h.Config?.Url))];
        GitHubHookDto? existing =
            matches.FirstOrDefault(h => string.Equals(h.Config?.Url, spec.PayloadUrl, StringComparison.Ordinal))
            ?? matches.FirstOrDefault();

        GitHubHookWriteDto payload = BuildHookPayload(spec, includeName: existing is null);

        (HttpMethod method, Uri writeUri, WebhookUpsertKind kind) = existing is null
            ? (HttpMethod.Post, hooksUri, WebhookUpsertKind.Created)
            : (HttpMethod.Patch, HookUri(baseUri, owner, repo, existing.Id), WebhookUpsertKind.Updated);

        using HttpRequestMessage writeRequest = CreateRulesetRequest(method, writeUri, token);
        writeRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, WebhookJsonOptions),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage writeResponse = await httpClient.SendAsync(writeRequest, cancellationToken);

        if (!writeResponse.IsSuccessStatusCode)
        {
            return Result<WebhookUpsertKind>.Fail(
                await ErrorFromWebhookFailureAsync(writeResponse, slug, RegisteringAction, cancellationToken));
        }

        RecordRestHeadroom(writeResponse);

        foreach (GitHubHookDto duplicate in matches.Where(h => h.Id != existing?.Id))
        {
            Result deleted = await DeleteHookAsync(baseUri, owner, repo, duplicate.Id, slug, token, cancellationToken);
            if (deleted is Result.Failure failure)
            {
                logger.LogWarning(
                    "Deleting duplicate webhook {HookId} on {Slug} failed ({ErrorCode}); it stays on the provider.",
                    duplicate.Id,
                    slug,
                    failure.Error.Code);
            }
        }

        return Result<WebhookUpsertKind>.Ok(kind);
    }

    /// <summary>
    /// Deletes every repository hook whose <c>config.url</c> carries the repository's hook id
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
            return Result<int>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri baseUri = EnsureTrailingSlash(apiBaseUrl);

        Result<List<GitHubHookDto>> listed = await ListHooksAsync(baseUri, owner, repo, slug, token, DeletingAction, cancellationToken);
        if (listed is not Result<List<GitHubHookDto>>.Success { Value: { } hooks })
        {
            return Result<int>.Fail(((Result<List<GitHubHookDto>>.Failure)listed).Error);
        }

        int deletedCount = 0;
        foreach (GitHubHookDto hook in hooks.Where(h => hookPath.Matches(h.Config?.Url)))
        {
            Result deleted = await DeleteHookAsync(baseUri, owner, repo, hook.Id, slug, token, cancellationToken);
            if (deleted is Result.Failure failure)
            {
                return Result<int>.Fail(failure.Error);
            }

            deletedCount++;
        }

        return Result<int>.Ok(deletedCount);
    }

    private const string RegisteringAction = "Registering the webhook";

    private const string DeletingAction = "Deleting the webhooks";

    private static Uri HookUri(Uri baseUri, string owner, string repo, long hookId) =>
        new(baseUri, $"repos/{owner}/{repo}/hooks/{hookId}");

    private async Task<Result<List<GitHubHookDto>>> ListHooksAsync(
        Uri baseUri,
        string owner,
        string repo,
        RepositorySlug slug,
        string token,
        string action,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage listRequest = CreateRulesetRequest(
            HttpMethod.Get,
            new Uri(baseUri, $"repos/{owner}/{repo}/hooks?per_page=100"),
            token);
        using HttpResponseMessage listResponse = await httpClient.SendAsync(listRequest, cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            return Result<List<GitHubHookDto>>.Fail(
                await ErrorFromWebhookFailureAsync(listResponse, slug, action, cancellationToken));
        }

        RecordRestHeadroom(listResponse);

        string listBody = await listResponse.Content.ReadAsStringAsync(cancellationToken);
        return Result<List<GitHubHookDto>>.Ok(
            JsonSerializer.Deserialize<List<GitHubHookDto>>(listBody, JsonOptions) ?? []);
    }

    private async Task<Result> DeleteHookAsync(
        Uri baseUri,
        string owner,
        string repo,
        long hookId,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRulesetRequest(HttpMethod.Delete, HookUri(baseUri, owner, repo, hookId), token);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail(await ErrorFromWebhookFailureAsync(response, slug, DeletingAction, cancellationToken));
        }

        RecordRestHeadroom(response);
        return Result.Ok();
    }

    internal static GitHubHookWriteDto BuildHookPayload(WebhookHookSpec spec, bool includeName) =>
        new(
            Name: includeName ? "web" : null,
            Active: WebhookHookSettings.GitHubActive,
            Events: [.. GitHubWebhookEventMapper.SubscribedEvents],
            Config: new GitHubHookConfigWriteDto(
                Url: spec.PayloadUrl,
                ContentType: WebhookHookSettings.GitHubContentType,
                Secret: spec.Secret,
                InsecureSsl: WebhookHookSettings.GitHubInsecureSsl));

    private static async Task<Error> ErrorFromWebhookFailureAsync(
        HttpResponseMessage response,
        RepositorySlug slug,
        string action,
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
                $"{action} on {slug} returned {(int)response.StatusCode} — the account " +
                "token needs the Webhooks (read and write) permission on the repository." +
                (providerMessage is null ? string.Empty : $" GitHub: {providerMessage}"));
        }

        int statusCode = (int)response.StatusCode;
        return GitHubErrors.ProviderError(
            $"{action} on {slug} returned {statusCode}" +
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
