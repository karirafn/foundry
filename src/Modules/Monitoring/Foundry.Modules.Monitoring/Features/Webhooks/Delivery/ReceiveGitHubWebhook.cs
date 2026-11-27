using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Webhooks.Ingress;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks.Delivery;

/// <summary>
/// Receives one GitHub webhook delivery (ADR 0085). Verification fails fast at the boundary: an unknown
/// hook id is 404; a missing or wrong <c>X-Hub-Signature-256</c> is 401 and the body is never parsed;
/// a body for another repository is 400. A verified delivery marks the hook healthy, enqueues every
/// nudge it maps to and returns 202 without calling the provider.
/// </summary>
internal static class ReceiveGitHubWebhook
{
    internal sealed class Handler(
        DbContext dbContext,
        IIssueQueries issueQueries,
        ITriageFailedIssueQueries triageFailedQueries,
        ISpecWatchedIssueQueries specWatchedQueries,
        WebhookNudgeQueue nudgeQueue,
        ILogger<Handler> logger)
    {
        public async Task<IResult> HandleAsync(string hookId, HttpRequest request, CancellationToken cancellationToken)
        {
            MonitoredRepository? repository = await WebhookDelivery.FindRepositoryAsync(dbContext, hookId, cancellationToken);
            if (repository is null)
            {
                return Results.NotFound();
            }

            byte[]? body = await WebhookDelivery.ReadBodyAsync(request, cancellationToken);
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

            using JsonDocument? document = WebhookDelivery.TryParse(body);
            if (document is null || !WebhookDelivery.NamesRepository(document.RootElement, "repository", "full_name", repository))
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
                ? await WebhookDelivery.LoadTrackingContextAsync(
                    issueQueries,
                    triageFailedQueries,
                    specWatchedQueries,
                    repository.Id,
                    GitHubWebhookEventMapper.IsIssueEvent(eventName),
                    cancellationToken)
                : WebhookTrackingContext.Empty;

            IReadOnlyList<NudgeKey> keys = GitHubWebhookEventMapper.Map(eventName, document.RootElement, repository.Id, tracked);

            if (keys.Count == 0)
            {
                logger.LogDebug(
                    "Dropped irrelevant '{Event}' delivery for repository {Slug}.",
                    eventName,
                    repository.Slug);
                return Results.Accepted();
            }

            foreach (NudgeKey key in keys)
            {
                nudgeQueue.TryEnqueue(new Nudge(key));
            }

            return Results.Accepted();
        }
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
