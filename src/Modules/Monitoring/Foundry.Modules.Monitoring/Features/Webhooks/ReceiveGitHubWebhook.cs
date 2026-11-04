using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Receives one GitHub webhook delivery (ADR 0085). Verification fails fast at the boundary: an unknown
/// hook id is 404; a missing or wrong <c>X-Hub-Signature-256</c> is 401 and the body is never parsed;
/// a body for another repository is 400. A verified delivery marks the hook healthy, enqueues at most one
/// nudge and returns 202 without calling the provider.
/// </summary>
internal static class ReceiveGitHubWebhook
{
    /// <summary>Upper bound on a delivery body; larger bodies are rejected with 413 before verification.</summary>
    internal const int MaxBodyBytes = 5 * 1024 * 1024;

    internal sealed class Handler(
        DbContext dbContext,
        IIssueQueries issueQueries,
        WebhookNudgeQueue nudgeQueue,
        ILogger<Handler> logger)
    {
        public async Task<IResult> HandleAsync(string hookId, HttpRequest request, CancellationToken cancellationToken)
        {
            if (!IsWellFormedHookId(hookId))
            {
                return Results.NotFound();
            }

            MonitoredRepository? repository = await dbContext.Set<MonitoredRepository>()
                .FirstOrDefaultAsync(r => r.WebhookHookId == hookId, cancellationToken);

            if (repository is null)
            {
                return Results.NotFound();
            }

            byte[]? body = await ReadBodyAsync(request, cancellationToken);
            if (body is null)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            string? signature = request.Headers[GitHubWebhookSignature.HeaderName];
            if (!GitHubWebhookSignature.IsValid(body, signature, repository.WebhookSecret))
            {
                logger.LogWarning(
                    "Rejected webhook delivery for repository {Slug}: missing or invalid signature.",
                    repository.Slug);
                return Results.Unauthorized();
            }

            string? eventName = request.Headers[GitHubWebhookEventMapper.EventHeaderName];
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return Results.BadRequest();
            }

            using JsonDocument? document = TryParse(body);
            if (document is null || !MatchesRepository(document.RootElement, repository))
            {
                logger.LogWarning(
                    "Rejected verified webhook delivery for repository {Slug}: the body names another repository.",
                    repository.Slug);
                return Results.BadRequest();
            }

            if (repository.RecordWebhookDelivery())
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Webhook for repository {Slug} is now active.", repository.Slug);
            }

            if (eventName == GitHubWebhookEventMapper.PingEvent || !repository.IsActive)
            {
                return Results.Accepted();
            }

            WebhookTrackingContext tracked = GitHubWebhookEventMapper.NeedsTrackingContext(eventName)
                ? await LoadTrackingContextAsync(repository, cancellationToken)
                : WebhookTrackingContext.Empty;

            NudgeKey? key = GitHubWebhookEventMapper.Map(eventName, document.RootElement, repository.Id, tracked);
            if (key is { } nudgeKey)
            {
                nudgeQueue.TryEnqueue(new Nudge(nudgeKey));
            }
            else
            {
                logger.LogDebug(
                    "Dropped irrelevant '{Event}' delivery for repository {Slug}.",
                    eventName,
                    repository.Slug);
            }

            return Results.Accepted();
        }

        private async Task<WebhookTrackingContext> LoadTrackingContextAsync(
            MonitoredRepository repository,
            CancellationToken cancellationToken)
        {
            IReadOnlySet<int> known = await issueQueries.GetKnownIssueNumbersAsync(repository.Id, cancellationToken);
            IReadOnlyList<ReviewIssueInfo> review = await issueQueries.GetReviewIssuesAsync(repository.Id, cancellationToken);
            IReadOnlyList<ChecksPendingIssueInfo> checksPending = await issueQueries.GetChecksPendingIssuesAsync(
                repository.Id,
                cancellationToken);
            return new WebhookTrackingContext(known, review, checksPending);
        }

        private static bool IsWellFormedHookId(string hookId) =>
            hookId.Length == MonitoredRepository.WebhookHookIdLength
            && hookId.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'));

        private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            if (request.ContentLength is > MaxBodyBytes)
            {
                return null;
            }

            using MemoryStream buffer = new();
            byte[] chunk = new byte[16 * 1024];
            int read;
            while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    return null;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            return buffer.ToArray();
        }

        private static JsonDocument? TryParse(byte[] body)
        {
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool MatchesRepository(JsonElement root, MonitoredRepository repository) =>
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("repository", out JsonElement repositoryElement)
            && repositoryElement.ValueKind == JsonValueKind.Object
            && repositoryElement.TryGetProperty("full_name", out JsonElement fullName)
            && fullName.ValueKind == JsonValueKind.String
            && string.Equals(fullName.GetString(), repository.Slug.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    internal static class Endpoint
    {
        public static void Map(IEndpointRouteBuilder routes)
        {
            routes.MapPost(WebhookIngressState.GitHubPathPrefix + "{hookId}", static (
                    string hookId,
                    HttpRequest request,
                    Handler handler,
                    CancellationToken cancellationToken) => handler.HandleAsync(hookId, request, cancellationToken))
                .ExcludeFromDescription();
        }
    }
}
