using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class CreateRepository
{
    internal sealed record Command(
        Guid AccountId,
        string Slug,
        int? PollIntervalSeconds) : ICommand<RepositorySummary>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        internal const string SlugEmptyCode = "CreateRepository.SlugEmpty";
        internal const string PollIntervalNotPositiveCode = "CreateRepository.PollIntervalNotPositive";

        public Result Validate(Command command)
        {
            if (string.IsNullOrWhiteSpace(command.Slug))
            {
                return new Error(SlugEmptyCode, "Repository slug must not be empty.");
            }

            if (command.PollIntervalSeconds.HasValue && command.PollIntervalSeconds.Value <= 0)
            {
                return new Error(PollIntervalNotPositiveCode, "Poll interval must be a positive number of seconds.");
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

            Account? account = await dbContext.Set<Account>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

            if (account is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.AccountNotFound(accountId));
            }

            Result<RepositorySlug> slugResult = RepositorySlug.Create(command.Slug);
            if (slugResult is not Result<RepositorySlug>.Success slugSuccess)
            {
                Error error = ((Result<RepositorySlug>.Failure)slugResult).Error;
                return Result<RepositorySummary>.Fail(error);
            }

            RepositorySlug repositorySlug = slugSuccess.Value;

            bool slugExists = await dbContext.Set<MonitoredRepository>()
                .AsNoTracking()
                .AnyAsync(r => r.Slug == repositorySlug, cancellationToken);

            if (slugExists)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.DuplicateSlug(command.Slug));
            }

            TimeSpan? pollInterval = command.PollIntervalSeconds.HasValue
                ? TimeSpan.FromSeconds(command.PollIntervalSeconds.Value)
                : null;

            MonitoredRepository repository = MonitoredRepository.Create(
                repositorySlug,
                accountId,
                pollInterval);

            dbContext.Set<MonitoredRepository>().Add(repository);
            await dbContext.SaveChangesAsync(cancellationToken);

            RepositorySummary summary = new(
                repository.Id.Value,
                repository.Slug.ToString(),
                repository.AccountId.Value,
                account.Name,
                repository.PollInterval.HasValue ? (int?)repository.PollInterval.Value.TotalSeconds : null,
                repository.IsActive,
                repository.LastPolledAt);

            return Result<RepositorySummary>.Ok(summary);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string Slug, int? PollIntervalSeconds);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost(string.Empty, static async (
                    Guid accountId,
                    RequestBody body,
                    ICommandHandler<Command, RepositorySummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(accountId, body.Slug, body.PollIntervalSeconds);
                    Result<RepositorySummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Created<RepositorySummary>, NotFound<string>, Conflict<string>, BadRequest<string>>>(
                        repository => TypedResults.Created(
                            $"/api/accounts/{accountId}/repositories/{repository.Id}",
                            repository),
                        error => error.Code switch
                        {
                            RepositoryErrors.AccountNotFoundCode => TypedResults.NotFound(error.Message),
                            RepositoryErrors.DuplicateSlugCode => TypedResults.Conflict(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("CreateRepository")
                .WithSummary("Creates a new monitored repository for an account")
                .Produces<RepositorySummary>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
