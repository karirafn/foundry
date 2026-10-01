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
                repository.Position,
                repository.LastPolledAt,
                RepositoryMappings.ToEligibilityInfo(repository.Eligibility),
                repository.UntrackSuppressedSince);

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

                    return result.Match<Results<Ok<RepositorySummary>, NotFound, BadRequest<string>>>(
                        repository => TypedResults.Ok(repository),
                        error => error.Code switch
                        {
                            RepositoryErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdateRepository")
                .WithSummary("Updates an existing monitored repository")
                .Produces<RepositorySummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
