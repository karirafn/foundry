using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Queries;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Issues.Features;

internal static class GetIssueById
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/{id:guid}", static async (
                    Guid id,
                    IIssueQueries queries,
                    CancellationToken cancellationToken) =>
                {
                    Result<IssueDetail> result = await queries.GetIssueDetailAsync(
                        IssueId.From(id),
                        cancellationToken);

                    return result.Match(
                        detail => TypedResults.Ok(detail) as IResult,
                        _ => TypedResults.NotFound());
                })
                .WithName("GetIssueById")
                .WithSummary("Gets issue detail by ID")
                .Produces<IssueDetail>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
