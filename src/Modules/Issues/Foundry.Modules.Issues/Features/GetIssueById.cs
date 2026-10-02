using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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

                    return result.Match<Results<Ok<IssueDetail>, ProblemHttpResult>>(
                        detail => TypedResults.Ok(detail),
                        error => error.ToProblem(StatusCodes.Status404NotFound));
                })
                .WithName("GetIssueById")
                .WithSummary("Gets issue detail by ID")
                .Produces<IssueDetail>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
