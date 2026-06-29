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
using Microsoft.EntityFrameworkCore.Storage;

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
        internal const string PollIntervalTooLargeCode = "CreateRepository.PollIntervalTooLarge";
        internal const int MaxPollIntervalSeconds = 86400;

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

            if (command.PollIntervalSeconds.HasValue && command.PollIntervalSeconds.Value > MaxPollIntervalSeconds)
            {
                return new Error(PollIntervalTooLargeCode, $"Poll interval must not exceed {MaxPollIntervalSeconds} seconds.");
            }

            return Result.Ok();
        }
    }

    internal sealed class Handler(
        DbContext dbContext,
        IIssueProviderFactory providerFactory,
        IRepositoryEligibilityEvaluator eligibilityEvaluator) : ICommandHandler<Command, RepositorySummary>
    {
        // The slug unique index name, used to identify slug-collision DbUpdateExceptions
        // and distinguish them from position-collision exceptions.
        private const string SlugIndexName = "ix_monitored_repositories_host_slug";

        private static bool IsSlugConstraintViolation(DbUpdateException ex, RepositorySlug slug)
        {
            string slugValue = slug.ToString();
            string message = ex.InnerException?.Message ?? ex.Message;
            return message.Contains(SlugIndexName, StringComparison.OrdinalIgnoreCase)
                || message.Contains(slugValue, StringComparison.OrdinalIgnoreCase);
        }

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
            if (slugResult is Result<RepositorySlug>.Failure slugFailure)
            {
                return Result<RepositorySummary>.Fail(slugFailure.Error);
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

            MonitoredRepository repository = MonitoredRepository.Create(
                repositorySlug,
                accountId,
                account.BaseUrl.Value.Host,
                pollInterval,
                position);

            dbContext.Set<MonitoredRepository>().Add(repository);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsSlugConstraintViolation(ex, repositorySlug))
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.DuplicateSlug(command.Slug));
            }
            catch (DbUpdateException)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.ConflictOnCreate());
            }

            if (!string.IsNullOrEmpty(account.Token))
            {
                IIssueProvider provider = providerFactory.CreateProvider(account, account.Token);
                await eligibilityEvaluator.EvaluateAndStoreAsync(repository, provider, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

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
                RepositoryMappings.ToEligibilityInfo(repository.Eligibility),
                repository.Position);

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
                            RepositoryErrors.ConflictOnCreateCode => TypedResults.Conflict(error.Message),
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
