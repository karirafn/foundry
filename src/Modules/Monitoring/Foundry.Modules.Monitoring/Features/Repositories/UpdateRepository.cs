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
        bool IsActive) : ICommand<RepositorySummary>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        internal const string PollIntervalNotPositiveCode = "UpdateRepository.PollIntervalNotPositive";
        internal const string PollIntervalTooLargeCode = "UpdateRepository.PollIntervalTooLarge";
        internal const int MaxPollIntervalSeconds = 86400;

        public Result Validate(Command command)
        {
            if (command.PollIntervalSeconds.HasValue && command.PollIntervalSeconds.Value <= 0)
            {
                return new Error(PollIntervalNotPositiveCode, "Poll interval must be a positive number of seconds.");
            }

            if (command.PollIntervalSeconds.HasValue && command.PollIntervalSeconds.Value > MaxPollIntervalSeconds)
            {
                return new Error(PollIntervalTooLargeCode, $"Poll interval must not exceed {MaxPollIntervalSeconds} seconds.");
            }

            return Result.Ok();
        }
    }

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, RepositorySummary>
    {
        public async Task<Result<RepositorySummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            AccountId accountId = AccountId.From(command.AccountId);
            MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(command.Id);

            MonitoredRepository? repository = await dbContext.Set<MonitoredRepository>()
                .Where(r => r.Id == repositoryId)
                .FirstOrDefaultAsync(r => r.AccountId == accountId, cancellationToken);

            if (repository is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            Account? account = await dbContext.Set<Account>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

            if (account is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.AccountNotFound(accountId));
            }

            TimeSpan? pollInterval = command.PollIntervalSeconds.HasValue
                ? TimeSpan.FromSeconds(command.PollIntervalSeconds.Value)
                : null;

            repository.Update(pollInterval, command.IsActive);

            await dbContext.SaveChangesAsync(cancellationToken);

            RepositorySummary summary = new(
                repository.Id.Value,
                repository.Slug.ToString(),
                repository.AccountId.Value,
                account.Name,
                account switch
                {
                    GitHubAccount => ProviderTypes.GitHub,
                    GitLabAccount => ProviderTypes.GitLab,
                    _ => throw new UnreachableException(),
                },
                RepositoryMappings.ToSeconds(repository.PollInterval),
                repository.IsActive,
                repository.LastPolledAt,
                RepositoryMappings.ToEligibilityInfo(repository.Eligibility));

            return Result<RepositorySummary>.Ok(summary);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(int? PollIntervalSeconds, bool IsActive);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("{id:guid}", static async (
                    Guid accountId,
                    Guid id,
                    RequestBody body,
                    ICommandHandler<Command, RepositorySummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(accountId, id, body.PollIntervalSeconds, body.IsActive);
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
