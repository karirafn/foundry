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
            CredentialId credentialId = CredentialId.From(query.AccountId);

            // Project into an anonymous type first to avoid EF limitations with
            // strongly-typed IDs and nullable TimeSpan arithmetic in the SELECT clause.
            var repositorySummaryRows = await dbContext.Set<MonitoredRepository>()
                .AsNoTracking()
                .Where(r => r.CredentialId == credentialId)
                .Join(
                    dbContext.Set<Credential>(),
                    r => r.CredentialId,
                    a => a.Id,
                    (r, a) => new
                    {
                        r.Id,
                        Slug = r.Slug.ToString(),
                        r.CredentialId,
                        AccountName = a.Name,
                        AccountType = EF.Property<string>(a, "type"),
                        r.PollInterval,
                        r.IsActive,
                        r.LastPolledAt,
                        r.Eligibility,
                        r.Position,
                    })
                .OrderBy(r => r.Position)
                .ToListAsync(cancellationToken);

            List<RepositorySummary> repositories = repositorySummaryRows
                .Select(r => new RepositorySummary(
                    r.Id.Value,
                    r.Slug,
                    r.CredentialId.Value,
                    r.AccountName,
                    r.AccountType,
                    RepositoryMappings.ToSeconds(r.PollInterval),
                    r.IsActive,
                    r.LastPolledAt,
                    RepositoryMappings.ToEligibilityInfo(r.Eligibility),
                    r.Position))
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
                .Produces<IReadOnlyList<RepositorySummary>>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
