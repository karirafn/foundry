using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RecheckRepositoryEligibility
{
    internal sealed record Command(Guid AccountId, Guid Id) : ICommand<RepositorySummary>;

    internal sealed class Handler(
        DbContext dbContext,
        IRepositoryEligibilityEvaluator eligibilityEvaluator,
        INamespaceDeriver namespaceDeriver,
        ILogger<Handler> logger) : ICommandHandler<Command, RepositorySummary>
    {
        public async Task<Result<RepositorySummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            CredentialId credentialId = CredentialId.From(command.AccountId);
            MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(command.Id);

            MonitoredRepository? repository = await dbContext.Set<MonitoredRepository>()
                .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

            if (repository is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            Credential? credential = await dbContext.Set<Credential>()
                .Include(c => c.Namespaces)
                .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

            if (credential is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.AccountNotFound(credentialId));
            }

            // Refresh the credential's namespace set from the live writable-repo listing so that
            // newly-granted namespaces become covered before re-evaluating eligibility.
            await RefreshNamespacesAsync(credential, cancellationToken);

            await eligibilityEvaluator.EvaluateFullyAndStoreAsync(repository, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            RepositorySummary summary = new(
                repository.Id.Value,
                repository.Slug.ToString(),
                credential.Id.Value,
                credential.Name,
                credential switch
                {
                    GitHubCredential => ProviderTypes.GitHub,
                    GitLabCredential => ProviderTypes.GitLab,
                    _ => throw new UnreachableException(),
                },
                RepositoryMappings.ToSeconds(repository.PollInterval),
                repository.IsActive,
                repository.LastPolledAt,
                RepositoryMappings.ToEligibilityInfo(repository.Eligibility),
                repository.Position);

            return Result<RepositorySummary>.Ok(summary);
        }

        private async Task RefreshNamespacesAsync(Credential credential, CancellationToken cancellationToken)
        {
            NamespaceDerivationOutcome outcome = await namespaceDeriver.DeriveAsync(credential, cancellationToken);

            if (outcome is NamespaceDerivationOutcome.Derived derived)
            {
                Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers =
                    await dbContext.FindClaimedNamespacesAsync(
                        credential.Host,
                        excludingCredentialId: credential.Id.Value,
                        cancellationToken);
                HashSet<string> claimedValues = [..claimedByOthers.Keys];
                credential.SetNamespaces(derived.Namespaces, claimedValues);
            }
            else
            {
                logger.LogWarning(
                    "Namespace refresh unavailable for credential {CredentialId}; evaluating eligibility against cached namespaces.",
                    credential.Id);
            }
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("{id:guid}/recheck", static async (
                    Guid accountId,
                    Guid id,
                    ICommandHandler<Command, RepositorySummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(accountId, id);
                    Result<RepositorySummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<RepositorySummary>, NotFound<string>, UnprocessableEntity<string>, ProblemHttpResult>>(
                        repository => TypedResults.Ok(repository),
                        error => error.Code switch
                        {
                            RepositoryErrors.NotFoundCode => TypedResults.NotFound(error.Message),
                            RepositoryErrors.AccountNotFoundCode => TypedResults.NotFound(error.Message),
                            RepositoryErrors.NoTokenCode => TypedResults.UnprocessableEntity(error.Message),
                            _ => TypedResults.Problem(error.Message),
                        });
                })
                .WithName("RecheckRepositoryEligibility")
                .WithSummary("Re-evaluates branch protection eligibility for a monitored repository")
                .Produces<RepositorySummary>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
