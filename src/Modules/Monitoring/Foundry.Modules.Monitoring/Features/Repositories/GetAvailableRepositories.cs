using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class GetAvailableRepositories
{
    internal sealed record Query(Guid AccountId) : IQuery<IReadOnlyList<AvailableRepository>>;

    internal sealed class Handler(DbContext dbContext, GitHubHttpClient gitHubHttpClient)
        : IQueryHandler<Query, IReadOnlyList<AvailableRepository>>
    {
        public async Task<Result<IReadOnlyList<AvailableRepository>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            Contracts.AccountId accountId = Contracts.AccountId.From(query.AccountId);

            Account? account = await dbContext.Set<Account>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

            if (account is null)
            {
                return Result<IReadOnlyList<AvailableRepository>>.Fail(
                    RepositoryErrors.AccountNotFound(accountId));
            }

            string token = account.Token ?? string.Empty;

            return await gitHubHttpClient.ListRepositoriesAsync(
                account.ApiBaseUrl,
                token,
                cancellationToken);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("available-repositories", static async (
                    Guid accountId,
                    IQueryHandler<Query, IReadOnlyList<AvailableRepository>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IReadOnlyList<AvailableRepository>> result = await handler.HandleAsync(
                        new Query(accountId),
                        cancellationToken);

                    return result.Match<Results<Ok<IReadOnlyList<AvailableRepository>>, NotFound<string>, BadRequest<string>>>(
                        repositories => TypedResults.Ok(repositories),
                        error => error.Code switch
                        {
                            RepositoryErrors.AccountNotFoundCode => TypedResults.NotFound(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("GetAvailableRepositories")
                .WithSummary("Gets all repositories available for a given account")
                .Produces<IReadOnlyList<AvailableRepository>>();
        }
    }
}
