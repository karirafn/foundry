using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Queries;
using Foundry.Modules.Monitoring.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Issues.Features;

internal static class GetIssueCounts
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/counts", static async (
                    Guid? repositoryId,
                    IIssueQueries queries,
                    CancellationToken cancellationToken) =>
                {
                    MonitoredRepositoryId? repoId = repositoryId.HasValue
                        ? MonitoredRepositoryId.From(repositoryId.Value)
                        : null;

                    IssueStateCounts counts = await queries.GetIssueStateCountsAsync(repoId, cancellationToken);

                    return TypedResults.Ok(counts);
                })
                .WithName("GetIssueCounts")
                .WithSummary("Gets issue counts grouped by state, optionally filtered by repository")
                .Produces<IssueStateCounts>()
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
