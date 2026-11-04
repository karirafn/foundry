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

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// Applies Foundry's default-branch rules to an already-monitored repository on explicit operator
/// action (ADR 0084). Runs the same <see cref="BranchRuleEnforcer"/> as the add flow and records the
/// action as an opt-in to the persisted <see cref="MonitoredRepository.ApplyBranchRules"/> intent.
/// </summary>
internal static class ApplyBranchRules
{
    internal sealed record Command(Guid AccountId, Guid Id) : ICommand<ApplyBranchRulesResponse>;

    internal sealed class Handler(
        DbContext dbContext,
        BranchRuleEnforcer branchRuleEnforcer,
        IGlobalSettingsQueries globalSettingsQueries,
        WebhookIngress? webhookIngress = null) : ICommandHandler<Command, ApplyBranchRulesResponse>
    {
        public async Task<Result<ApplyBranchRulesResponse>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            CredentialId credentialId = CredentialId.From(command.AccountId);
            MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(command.Id);

            MonitoredRepository? repository = await dbContext.Set<MonitoredRepository>()
                .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

            if (repository is null)
            {
                return Result<ApplyBranchRulesResponse>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            Credential? credential = await dbContext.Set<Credential>()
                .AsNoTracking()
                .Include(c => c.Namespaces)
                .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

            if (credential is null)
            {
                return Result<ApplyBranchRulesResponse>.Fail(RepositoryErrors.AccountNotFound(credentialId));
            }

            // The write uses this account's branch-rules token, so the repository must be one the
            // account lists (same host, covered by its namespaces) — never another account's repo.
            if (repository.Host != credential.Host || !credential.Covers(repository.Slug))
            {
                return Result<ApplyBranchRulesResponse>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            repository.OptInToBranchRules();

            BranchRulesOutcome branchRules = await branchRuleEnforcer.EvaluateAndApplyAsync(
                repository,
                credential,
                applyBranchRules: true,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

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

            return Result<ApplyBranchRulesResponse>.Ok(new ApplyBranchRulesResponse(summary, branchRules));
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("{id:guid}/apply-branch-rules", static async (
                    Guid accountId,
                    Guid id,
                    ICommandHandler<Command, ApplyBranchRulesResponse> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(accountId, id);
                    Result<ApplyBranchRulesResponse> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<ApplyBranchRulesResponse>, ProblemHttpResult>>(
                        response => TypedResults.Ok(response),
                        error => error.Code switch
                        {
                            RepositoryErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            RepositoryErrors.AccountNotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("ApplyBranchRules")
                .WithSummary("Applies Foundry's default-branch rules to a monitored repository")
                .Produces<ApplyBranchRulesResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
