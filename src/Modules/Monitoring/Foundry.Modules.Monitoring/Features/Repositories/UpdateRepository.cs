using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class UpdateRepository
{
    internal sealed record Command(
        Guid AccountId,
        Guid Id,
        int? PollIntervalSeconds,
        bool IsActive,
        int MaxConcurrentWorkers) : ICommand<RepositorySummary>;

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, RepositorySummary>
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

            TimeSpan? pollInterval = command.PollIntervalSeconds.HasValue
                ? TimeSpan.FromSeconds(command.PollIntervalSeconds.Value)
                : null;

            Result updateResult = repository.Update(pollInterval, command.IsActive, command.MaxConcurrentWorkers);
            if (updateResult is Result.Failure updateFailure)
            {
                return Result<RepositorySummary>.Fail(updateFailure.Error);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            string providerType = credential switch
            {
                GitHubCredential => ProviderTypes.GitHub,
                GitLabCredential => ProviderTypes.GitLab,
                _ => throw new UnreachableException(),
            };

            RepositorySummary summary = RepositoryMappings.ToSummary(
                repository,
                credential.Id.Value,
                credential.Name,
                providerType,
                defaultPollIntervalSeconds: 0);

            return Result<RepositorySummary>.Ok(summary);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(int? PollIntervalSeconds, bool IsActive, int MaxConcurrentWorkers);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("{id:guid}", static async (
                    Guid accountId,
                    Guid id,
                    RequestBody body,
                    ICommandHandler<Command, RepositorySummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(accountId, id, body.PollIntervalSeconds, body.IsActive, body.MaxConcurrentWorkers);
                    Result<RepositorySummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<RepositorySummary>, ProblemHttpResult>>(
                        repository => TypedResults.Ok(repository),
                        error => error.Code switch
                        {
                            RepositoryErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            RepositoryErrors.AccountNotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateRepository")
                .WithSummary("Updates an existing monitored repository")
                .Produces<RepositorySummary>()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
