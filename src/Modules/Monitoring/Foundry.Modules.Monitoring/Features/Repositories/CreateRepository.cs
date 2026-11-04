using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class CreateRepository
{
    internal sealed record Command(
        Guid AccountId,
        string Slug,
        int? PollIntervalSeconds,
        int? MaxConcurrentWorkers = null,
        bool ApplyBranchRules = true) : ICommand<CreateRepositoryResponse>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        internal const string SlugEmptyCode = "CreateRepository.SlugEmpty";

        public Result Validate(Command command)
        {
            if (string.IsNullOrWhiteSpace(command.Slug))
            {
                return new Error(SlugEmptyCode, "Repository slug must not be empty.");
            }

            return Result.Ok();
        }
    }

    internal sealed class Handler(
        DbContext dbContext,
        BranchRuleEnforcer branchRuleEnforcer,
        IGlobalSettingsQueries globalSettingsQueries,
        WebhookIngress? webhookIngress = null,
        WebhookRegistrar? webhookRegistrar = null) : ICommandHandler<Command, CreateRepositoryResponse>
    {
        // SQLite emits the column-reference form ("monitored_repositories.slug") rather than the
        // index name on unique-constraint violations, so both forms must be checked.
        private const string SlugIndexName = "ix_monitored_repositories_host_slug";
        private const string SlugColumnReference = "monitored_repositories.slug";

        private static bool IsSlugConstraintViolation(DbUpdateException ex, RepositorySlug slug)
        {
            string slugValue = slug.ToString();
            string message = ex.InnerException?.Message ?? ex.Message;
            return message.Contains(SlugIndexName, StringComparison.OrdinalIgnoreCase)
                || message.Contains(SlugColumnReference, StringComparison.OrdinalIgnoreCase)
                || message.Contains(slugValue, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<Result<CreateRepositoryResponse>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            CredentialId credentialId = CredentialId.From(command.AccountId);

            Credential? credential = await dbContext.Set<Credential>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

            if (credential is null)
            {
                return Result<CreateRepositoryResponse>.Fail(RepositoryErrors.AccountNotFound(credentialId));
            }

            Result<RepositorySlug> slugResult = RepositorySlug.Create(command.Slug);
            if (slugResult is Result<RepositorySlug>.Failure slugFailure)
            {
                return Result<CreateRepositoryResponse>.Fail(slugFailure.Error);
            }

            if (slugResult is not Result<RepositorySlug>.Success slugSuccess)
            {
                throw new UnreachableException();
            }

            RepositorySlug repositorySlug = slugSuccess.Value;

            TimeSpan? pollInterval = command.PollIntervalSeconds.HasValue
                ? TimeSpan.FromSeconds(command.PollIntervalSeconds.Value)
                : null;

            await using IDbContextTransaction tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            int position = await dbContext.Set<MonitoredRepository>().CountAsync(cancellationToken);

            int maxConcurrentWorkers = command.MaxConcurrentWorkers ?? MonitoredRepository.DefaultMaxConcurrentWorkers;

            Result<MonitoredRepository> createResult = MonitoredRepository.Create(
                repositorySlug,
                credential.BaseUrl.Value.Host,
                pollInterval,
                position,
                maxConcurrentWorkers,
                command.ApplyBranchRules);

            if (createResult is Result<MonitoredRepository>.Failure createFailure)
            {
                return Result<CreateRepositoryResponse>.Fail(createFailure.Error);
            }

            MonitoredRepository repository = ((Result<MonitoredRepository>.Success)createResult).Value;

            dbContext.Set<MonitoredRepository>().Add(repository);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsSlugConstraintViolation(ex, repositorySlug))
            {
                return Result<CreateRepositoryResponse>.Fail(RepositoryErrors.DuplicateSlug(command.Slug));
            }
            catch (DbUpdateException)
            {
                return Result<CreateRepositoryResponse>.Fail(RepositoryErrors.ConflictOnCreate());
            }

            // The repository is already committed: a failed or skipped rules write never fails the add.
            BranchRulesOutcome branchRules = await branchRuleEnforcer.EvaluateAndApplyAsync(
                repository,
                credential,
                command.ApplyBranchRules,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Likewise a skipped or failed webhook registration never fails the add: the repository
            // stays NotRegistered and the repository page shows the manual instructions.
            WebhookRegistrationOutcome? webhook = null;
            if (webhookRegistrar is not null)
            {
                webhook = await webhookRegistrar.RegisterAsync(repository, credential, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            string providerType = credential switch
            {
                GitHubCredential => ProviderTypes.GitHub,
                GitLabCredential => ProviderTypes.GitLab,
                _ => throw new UnreachableException(),
            };

            int defaultPollIntervalSeconds = await globalSettingsQueries.GetPollIntervalSecondsAsync(cancellationToken);

            bool webhookIngressConfigured = webhookIngress is not null
                && (await webhookIngress.GetStateAsync(cancellationToken)).IsConfigured;

            RepositorySummary summary = RepositoryMappings.ToSummary(
                repository,
                credential.Id.Value,
                credential.Name,
                providerType,
                defaultPollIntervalSeconds,
                webhookIngressConfigured);

            return Result<CreateRepositoryResponse>.Ok(new CreateRepositoryResponse(summary, branchRules, webhook));
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(
            string Slug,
            int? PollIntervalSeconds,
            int? MaxConcurrentWorkers = null,
            bool? ApplyBranchRules = null);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost(string.Empty, static async (
                    Guid accountId,
                    RequestBody body,
                    ICommandHandler<Command, CreateRepositoryResponse> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(
                        accountId,
                        body.Slug,
                        body.PollIntervalSeconds,
                        body.MaxConcurrentWorkers,
                        body.ApplyBranchRules ?? true);
                    Result<CreateRepositoryResponse> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Created<CreateRepositoryResponse>, ProblemHttpResult>>(
                        response => TypedResults.Created(
                            $"/api/accounts/{accountId}/repositories/{response.Repository.Id}",
                            response),
                        error => error.Code switch
                        {
                            RepositoryErrors.AccountNotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            RepositoryErrors.DuplicateSlugCode => error.ToProblem(StatusCodes.Status409Conflict),
                            RepositoryErrors.ConflictOnCreateCode => error.ToProblem(StatusCodes.Status409Conflict),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("CreateRepository")
                .WithSummary("Creates a new monitored repository for an account")
                .Produces<CreateRepositoryResponse>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
