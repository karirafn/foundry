using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Eligibility;
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
        int? MaxConcurrentWorkers = null) : ICommand<RepositorySummary>;

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
        IRepositoryEligibilityEvaluator eligibilityEvaluator) : ICommandHandler<Command, RepositorySummary>
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

        public async Task<Result<RepositorySummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            CredentialId credentialId = CredentialId.From(command.AccountId);

            Credential? credential = await dbContext.Set<Credential>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

            if (credential is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.AccountNotFound(credentialId));
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

            int maxConcurrentWorkers = command.MaxConcurrentWorkers ?? MonitoredRepository.DefaultMaxConcurrentWorkers;

            Result<MonitoredRepository> createResult = MonitoredRepository.Create(
                repositorySlug,
                credential.BaseUrl.Value.Host,
                pollInterval,
                position,
                maxConcurrentWorkers);

            if (createResult is Result<MonitoredRepository>.Failure createFailure)
            {
                return Result<RepositorySummary>.Fail(createFailure.Error);
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
                return Result<RepositorySummary>.Fail(RepositoryErrors.DuplicateSlug(command.Slug));
            }
            catch (DbUpdateException)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.ConflictOnCreate());
            }

            await eligibilityEvaluator.EvaluateFullyAndStoreAsync(repository, DateTimeOffset.UtcNow, cancellationToken);
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
                repository.MaxConcurrentWorkers,
                repository.LastPolledAt,
                RepositoryMappings.ToEligibilityInfo(repository.Eligibility),
                repository.UntrackSuppressedSince);

            return Result<RepositorySummary>.Ok(summary);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string Slug, int? PollIntervalSeconds, int? MaxConcurrentWorkers = null);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost(string.Empty, static async (
                    Guid accountId,
                    RequestBody body,
                    ICommandHandler<Command, RepositorySummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(accountId, body.Slug, body.PollIntervalSeconds, body.MaxConcurrentWorkers);
                    Result<RepositorySummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Created<RepositorySummary>, ProblemHttpResult>>(
                        repository => TypedResults.Created(
                            $"/api/accounts/{accountId}/repositories/{repository.Id}",
                            repository),
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
                .Produces<RepositorySummary>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
