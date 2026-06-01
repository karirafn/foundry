using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Issues.Features;

internal static class GetIssues
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/", static async (
                    Guid? repositoryId,
                    IIssueQueries queries,
                    CancellationToken cancellationToken) =>
                {
                    MonitoredRepositoryId? repoId = repositoryId.HasValue
                        ? MonitoredRepositoryId.From(repositoryId.Value)
                        : null;

                    IReadOnlyList<IssueSummary> summaries = await queries.GetIssueSummariesAsync(
                        repoId,
                        cancellationToken);

                    return TypedResults.Ok(summaries);
                })
                .WithName("GetIssues")
                .WithSummary("Gets issue summaries, optionally filtered by repository")
                .Produces<IReadOnlyList<IssueSummary>>()
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
