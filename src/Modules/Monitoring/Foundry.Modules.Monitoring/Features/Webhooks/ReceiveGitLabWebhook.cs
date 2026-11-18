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
/// Receives one GitLab webhook delivery (ADR 0085). Verification fails fast at the boundary: an unknown
/// hook id is 404; a missing or wrong <c>X-Gitlab-Token</c> is 401 and the body is never parsed; a body
/// whose <c>project.path_with_namespace</c> names another project is 400. GitLab has no <c>ping</c>, so
/// the first verified delivery of any kind (typically the hook form's "Test" action) activates the hook.
/// A verified delivery enqueues at most one nudge and returns 202 without calling the provider.
/// </summary>
internal static class ReceiveGitLabWebhook
{
    internal sealed class Handler(
        DbContext dbContext,
        IIssueQueries issueQueries,
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

            string? token = request.Headers[GitLabWebhookToken.HeaderName];
            if (!GitLabWebhookToken.IsValid(token, repository.WebhookSecret))
            {
                logger.LogWarning(
                    "Rejected GitLab webhook delivery for repository {Slug}: missing or invalid token.",
                    repository.Slug);
                return Results.Unauthorized();
            }

            using JsonDocument? document = WebhookDelivery.TryParse(body);
            if (document is null
                || !WebhookDelivery.NamesRepository(document.RootElement, "project", "path_with_namespace", repository))
            {
                logger.LogWarning(
                    "Rejected verified GitLab webhook delivery for repository {Slug}: the body names another project.",
                    repository.Slug);
                return Results.BadRequest();
            }

            if (repository.RecordWebhookDelivery())
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Webhook for repository {Slug} is now active.", repository.Slug);
            }

            if (!repository.IsActive)
            {
                return Results.Accepted();
            }

            string? objectKind = GitLabWebhookEventMapper.GetObjectKind(document.RootElement);

            WebhookTrackingContext tracked = GitLabWebhookEventMapper.NeedsTrackingContext(objectKind)
                ? await WebhookDelivery.LoadTrackingContextAsync(
                    issueQueries,
                    repository.Id,
                    GitLabWebhookEventMapper.NeedsBlockers(objectKind),
                    cancellationToken)
                : WebhookTrackingContext.Empty;

            NudgeKey? key = GitLabWebhookEventMapper.Map(document.RootElement, repository.Id, tracked);
            if (key is { } nudgeKey)
            {
                nudgeQueue.TryEnqueue(new Nudge(nudgeKey));
            }
            else
            {
                logger.LogDebug(
                    "Dropped irrelevant '{ObjectKind}' GitLab delivery for repository {Slug}.",
                    objectKind,
                    repository.Slug);
            }

            return Results.Accepted();
        }
    }

    internal static class Endpoint
    {
        public static void Map(IEndpointRouteBuilder routes)
        {
            routes.MapPost(WebhookIngressState.GitLabPathPrefix + "{hookId}", static (
                    string hookId,
                    HttpRequest request,
                    Handler handler,
                    CancellationToken cancellationToken) => handler.HandleAsync(hookId, request, cancellationToken))
                .ExcludeFromDescription();
        }
    }
}
