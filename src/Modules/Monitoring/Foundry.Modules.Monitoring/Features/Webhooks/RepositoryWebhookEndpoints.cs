using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Manual webhook registration for one repository (ADR 0085): the registration details, the explicit
/// secret reveal, and the operator's "I've added the webhook" action. All three are scoped to an
/// account that covers the repository, like the other repository endpoints.
/// </summary>
internal static class RepositoryWebhookEndpoints
{
    internal const string ContentType = "application/json";

    internal sealed class Handler(DbContext dbContext, WebhookIngress webhookIngress)
    {
        public async Task<Result<RepositoryWebhookInfo>> GetInfoAsync(
            Guid accountId,
            Guid id,
            CancellationToken cancellationToken)
        {
            Result<MonitoredRepository> repository = await LoadAsync(accountId, id, cancellationToken);
            return repository switch
            {
                Result<MonitoredRepository>.Success success => await ToInfoAsync(success.Value, cancellationToken),
                Result<MonitoredRepository>.Failure failure => Result<RepositoryWebhookInfo>.Fail(failure.Error),
                _ => throw new InvalidOperationException("Unexpected result."),
            };
        }

        public async Task<Result<RepositoryWebhookSecret>> RevealSecretAsync(
            Guid accountId,
            Guid id,
            CancellationToken cancellationToken)
        {
            Result<MonitoredRepository> repository = await LoadAsync(accountId, id, cancellationToken);
            if (repository is not Result<MonitoredRepository>.Success success)
            {
                return Result<RepositoryWebhookSecret>.Fail(((Result<MonitoredRepository>.Failure)repository).Error);
            }

            string secret = success.Value.EnsureWebhookSecret();
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<RepositoryWebhookSecret>.Ok(new RepositoryWebhookSecret(secret));
        }

        public async Task<Result<RepositoryWebhookInfo>> MarkRegisteredAsync(
            Guid accountId,
            Guid id,
            CancellationToken cancellationToken)
        {
            Result<MonitoredRepository> repository = await LoadAsync(accountId, id, cancellationToken);
            if (repository is not Result<MonitoredRepository>.Success success)
            {
                return Result<RepositoryWebhookInfo>.Fail(((Result<MonitoredRepository>.Failure)repository).Error);
            }

            success.Value.MarkWebhookRegistered();
            await dbContext.SaveChangesAsync(cancellationToken);
            return await ToInfoAsync(success.Value, cancellationToken);
        }

        private async Task<Result<RepositoryWebhookInfo>> ToInfoAsync(
            MonitoredRepository repository,
            CancellationToken cancellationToken)
        {
            WebhookIngressState ingress = await webhookIngress.GetStateAsync(cancellationToken);
            return Result<RepositoryWebhookInfo>.Ok(new RepositoryWebhookInfo(
                RepositoryMappings.ToWebhookStatusToken(repository.EffectiveWebhookStatus(ingress.IsConfigured)),
                ingress.GitHubPayloadUrl(repository.WebhookHookId),
                ContentType,
                GitHubWebhookEventMapper.SubscribedEvents,
                (int)ingress.BackstopInterval.TotalMinutes));
        }

        private async Task<Result<MonitoredRepository>> LoadAsync(
            Guid accountId,
            Guid id,
            CancellationToken cancellationToken)
        {
            MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(id);
            CredentialId credentialId = CredentialId.From(accountId);

            MonitoredRepository? repository = await dbContext.Set<MonitoredRepository>()
                .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

            Credential? credential = await dbContext.Set<Credential>()
                .AsNoTracking()
                .Include(c => c.Namespaces)
                .FirstOrDefaultAsync(c => c.Id == credentialId, cancellationToken);

            if (repository is null
                || credential is null
                || repository.Host != credential.Host
                || !credential.Covers(repository.Slug))
            {
                return Result<MonitoredRepository>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            return Result<MonitoredRepository>.Ok(repository);
        }
    }

    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("{id:guid}/webhook", static async (
                Guid accountId,
                Guid id,
                Handler handler,
                CancellationToken cancellationToken) =>
            {
                Result<RepositoryWebhookInfo> result = await handler.GetInfoAsync(accountId, id, cancellationToken);
                return result.Match<Results<Ok<RepositoryWebhookInfo>, ProblemHttpResult>>(
                    info => TypedResults.Ok(info),
                    error => error.ToProblem(StatusCodes.Status404NotFound));
            })
            .WithName("GetRepositoryWebhook")
            .WithSummary("Gets the manual webhook registration details for a repository (never the secret)")
            .Produces<RepositoryWebhookInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("{id:guid}/webhook/secret", static async (
                Guid accountId,
                Guid id,
                Handler handler,
                HttpResponse response,
                CancellationToken cancellationToken) =>
            {
                // The secret must never be cached by the browser or an intermediary.
                response.Headers.CacheControl = "no-store";
                Result<RepositoryWebhookSecret> result = await handler.RevealSecretAsync(accountId, id, cancellationToken);
                return result.Match<Results<Ok<RepositoryWebhookSecret>, ProblemHttpResult>>(
                    secret => TypedResults.Ok(secret),
                    error => error.ToProblem(StatusCodes.Status404NotFound));
            })
            .WithName("RevealRepositoryWebhookSecret")
            .WithSummary("Reveals the repository's webhook secret for manual registration")
            .Produces<RepositoryWebhookSecret>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("{id:guid}/webhook/registered", static async (
                Guid accountId,
                Guid id,
                Handler handler,
                CancellationToken cancellationToken) =>
            {
                Result<RepositoryWebhookInfo> result = await handler.MarkRegisteredAsync(accountId, id, cancellationToken);
                return result.Match<Results<Ok<RepositoryWebhookInfo>, ProblemHttpResult>>(
                    info => TypedResults.Ok(info),
                    error => error.ToProblem(StatusCodes.Status404NotFound));
            })
            .WithName("MarkRepositoryWebhookRegistered")
            .WithSummary("Records that the operator added the webhook on the provider; the hook waits for its first delivery")
            .Produces<RepositoryWebhookInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
