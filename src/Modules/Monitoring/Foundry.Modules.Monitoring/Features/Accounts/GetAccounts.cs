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
    internal sealed record Query : IQuery<IReadOnlyList<CredentialSummary>>;

    internal sealed class Handler(DbContext dbContext)
        : IQueryHandler<Query, IReadOnlyList<CredentialSummary>>
    {
        public async Task<Result<IReadOnlyList<CredentialSummary>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            var rows = await dbContext.Set<Credential>()
                .AsNoTracking()
                .Select(a => new
                {
                    a.Id,
                    a.Name,
                    ProviderType = EF.Property<string>(a, "type"),
                    a.BaseUrl,
                    a.Token,
                })
                .ToListAsync(cancellationToken);

            List<CredentialSummary> summaries = rows
                .Select(r => new CredentialSummary(
                    r.Id.Value,
                    r.Name,
                    r.ProviderType,
                    r.BaseUrl.Value.ToString(),
                    r.Token != null))
                .ToList();

            return Result<IReadOnlyList<CredentialSummary>>.Ok(summaries);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet(string.Empty, static async (
                    IQueryHandler<Query, IReadOnlyList<CredentialSummary>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<IReadOnlyList<CredentialSummary>> result = await handler.HandleAsync(
                        new Query(),
                        cancellationToken);

                    return result.Match<Results<Ok<IReadOnlyList<CredentialSummary>>, BadRequest<string>>>(
                        credentials => TypedResults.Ok(credentials),
                        error => TypedResults.BadRequest(error.Message));
                })
                .WithName("GetAccounts")
                .WithSummary("Gets all configured accounts")
                .Produces<IReadOnlyList<CredentialSummary>>()
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
