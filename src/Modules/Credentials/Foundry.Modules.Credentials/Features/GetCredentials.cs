using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Credentials.Features;

internal static class GetCredentials
{
    internal sealed record Query : IQuery<ClaudeAccountSummary>;

    internal sealed class Handler(DbContext dbContext) : IQueryHandler<Query, ClaudeAccountSummary>
    {
        public async Task<Result<ClaudeAccountSummary>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (account is null)
            {
                return Result<ClaudeAccountSummary>.Fail(CredentialsErrors.NotFound);
            }

            return CredentialQueries.ToSummary(account);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/", static async (
                    IQueryHandler<Query, ClaudeAccountSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<ClaudeAccountSummary> result = await handler.HandleAsync(
                        new Query(),
                        cancellationToken);

                    return result.Match<Results<Ok<ClaudeAccountSummary>, NotFound>>(
                        summary => TypedResults.Ok(summary),
                        _ => TypedResults.NotFound());
                })
                .WithName("GetCredentials")
                .WithSummary("Gets the current Claude account credentials summary")
                .Produces<ClaudeAccountSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
