using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class GetRepositories
{
    internal sealed record Query(Guid AccountId) : IQuery<IReadOnlyList<RepositorySummary>>;

    internal sealed class Handler(DbContext dbContext)
        : IQueryHandler<Query, IReadOnlyList<RepositorySummary>>
    {
        public async Task<Result<IReadOnlyList<RepositorySummary>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            AccountId accountId = AccountId.From(query.AccountId);

            // Project into an anonymous type first to avoid EF limitations with
            // strongly-typed IDs and nullable TimeSpan arithmetic in the SELECT clause.
            var rows = await dbContext.Set<MonitoredRepository>()
                .AsNoTracking()
                .Where(r => r.AccountId == accountId)
                .Join(
                    dbContext.Set<Account>().AsNoTracking(),
                    r => r.AccountId,
                    a => a.Id,
                    (r, a) => new
                    {
                        r.Id,
                        Slug = r.Slug.ToString(),
                        r.AccountId,
                        AccountName = a.Name,
                        r.PollInterval,
                        r.IsActive,
                        r.LastPolledAt,
                    })
                .ToListAsync(cancellationToken);

            List<RepositorySummary> repositories = rows
                .Select(r => new RepositorySummary(
                    r.Id.Value,
                    r.Slug,
                    r.AccountId.Value,
                    r.AccountName,
                    r.PollInterval.HasValue ? (int?)r.PollInterval.Value.TotalSeconds : null,
                    r.IsActive,
                    r.LastPolledAt))
                .ToList();

            return Result<IReadOnlyList<RepositorySummary>>.Ok(repositories);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet(string.Empty, static async (
                    Guid accountId,
                    IQueryHandler<Query, IReadOnlyList<RepositorySummary>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IReadOnlyList<RepositorySummary>> result = await handler.HandleAsync(
                        new Query(accountId),
                        cancellationToken);

                    return result.Match<Results<Ok<IReadOnlyList<RepositorySummary>>, BadRequest<string>>>(
                        repositories => TypedResults.Ok(repositories),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("GetRepositories")
                .WithSummary("Gets all monitored repositories for an account")
                .Produces<IReadOnlyList<RepositorySummary>>();
        }
    }
}
