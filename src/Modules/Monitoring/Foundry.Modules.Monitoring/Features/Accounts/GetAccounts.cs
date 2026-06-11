using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class GetAccounts
{
    internal sealed record Query : IQuery<IReadOnlyList<AccountSummary>>;

    internal sealed class Handler(DbContext dbContext)
        : IQueryHandler<Query, IReadOnlyList<AccountSummary>>
    {
        public async Task<Result<IReadOnlyList<AccountSummary>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            List<AccountSummary> accounts = await dbContext.Set<Account>()
                .AsNoTracking()
                .Select(a => new AccountSummary(
                    a.Id.Value,
                    a.Name,
                    EF.Property<string>(a, "type"),
                    a.BaseUrl.ToString(),
                    a.Token != null))
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<AccountSummary>>.Ok(accounts);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet(string.Empty, static async (
                    IQueryHandler<Query, IReadOnlyList<AccountSummary>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IReadOnlyList<AccountSummary>> result = await handler.HandleAsync(
                        new Query(),
                        cancellationToken);

                    return result.Match<Results<Ok<IReadOnlyList<AccountSummary>>, BadRequest<string>>>(
                        accounts => TypedResults.Ok(accounts),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("GetAccounts")
                .WithSummary("Gets all configured accounts")
                .Produces<IReadOnlyList<AccountSummary>>();
        }
    }
}
