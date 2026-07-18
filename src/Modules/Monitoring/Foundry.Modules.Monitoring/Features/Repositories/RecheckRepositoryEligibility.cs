using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RecheckRepositoryEligibility
{
    internal sealed record Command(Guid AccountId, Guid Id) : ICommand<RepositorySummary>;

    internal sealed class Handler(
        DbContext dbContext,
        IRepositoryEligibilityEvaluator eligibilityEvaluator) : ICommandHandler<Command, RepositorySummary>
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
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

            if (credential is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.AccountNotFound(credentialId));
            }

            await eligibilityEvaluator.EvaluateAndStoreAsync(repository, cancellationToken);
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
