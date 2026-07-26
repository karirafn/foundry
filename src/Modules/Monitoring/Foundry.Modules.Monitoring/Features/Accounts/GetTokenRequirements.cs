using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class GetTokenRequirements
{
    internal sealed record Query(string ProviderType) : IQuery<TokenRequirements>;

    internal sealed class Handler : IQueryHandler<Query, TokenRequirements>
    {
        public Task<Result<TokenRequirements>> HandleAsync(Query query, CancellationToken cancellationToken)
        {
            TokenRequirements? requirements = TokenRequirementsCatalog.For(query.ProviderType);

            if (requirements is null)
            {
                return Task.FromResult(Result<TokenRequirements>.Fail(new Error(
                    "provider.not_found",
                    $"No token requirements found for provider '{query.ProviderType}'.")
                {
                    Kind = ErrorKind.NotFound,
                }));
            }

            return Task.FromResult(Result<TokenRequirements>.Ok(requirements));
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/{provider}/token-requirements", static async (
                    string provider,
                    IQueryHandler<Query, TokenRequirements> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<TokenRequirements> result = await handler.HandleAsync(
                        new Query(provider),
                        cancellationToken);

                    return result.Match<Results<Ok<TokenRequirements>, NotFound>>(
                        requirements => TypedResults.Ok(requirements),
                        _ => TypedResults.NotFound());
                })
                .WithName("GetTokenRequirements")
                .WithSummary("Gets the PAT requirements for a provider")
                .Produces<TokenRequirements>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
