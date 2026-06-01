using Foundry.Modules.Issues.Contracts;

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
                    IssueDetail? detail = await queries.GetIssueDetailAsync(
                        IssueId.From(id),
                        cancellationToken);

                    if (detail is null)
                    {
                        return (Results<Ok<IssueDetail>, NotFound>)TypedResults.NotFound();
                    }

                    return (Results<Ok<IssueDetail>, NotFound>)TypedResults.Ok(detail);
                })
                .WithName("GetIssueById")
                .WithSummary("Gets issue detail by ID")
                .Produces<IssueDetail>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
