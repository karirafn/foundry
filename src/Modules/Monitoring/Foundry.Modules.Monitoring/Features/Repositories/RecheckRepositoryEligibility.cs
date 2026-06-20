using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
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
        IIssueProviderFactory providerFactory,
        IRepositoryEligibilityEvaluator eligibilityEvaluator) : ICommandHandler<Command, RepositorySummary>
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
                RepositoryMappings.ToSeconds(repository.PollInterval),
                repository.IsActive,
                repository.LastPolledAt,
                RepositoryMappings.ToEligibilityInfo(repository.Eligibility));

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

                    return result.Match<Results<Ok<RepositorySummary>, NotFound<string>, ProblemHttpResult>>(
                        repository => TypedResults.Ok(repository),
                        error => error.Code switch
                        {
                            RepositoryErrors.NotFoundCode => TypedResults.NotFound(error.Message),
                            RepositoryErrors.AccountNotFoundCode => TypedResults.NotFound(error.Message),
                            _ => TypedResults.Problem(error.Message),
                        });
                })
                .WithName("RecheckRepositoryEligibility")
                .WithSummary("Re-evaluates branch protection eligibility for a monitored repository")
                .Produces<RepositorySummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
