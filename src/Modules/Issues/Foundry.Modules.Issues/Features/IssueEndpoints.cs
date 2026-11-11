using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Issues.Features;

internal static class IssueEndpoints
{
    internal static IEndpointRouteBuilder MapIssueEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/issues")
            .WithTags("Issues");

        GetIssues.Endpoint.Map(group);
        GetIssueCounts.Endpoint.Map(group);
        GetIssueById.Endpoint.Map(group);
        RetryIssue.Endpoint.Map(group);
        RetryIssueFresh.Endpoint.Map(group);
        RetryTriage.Endpoint.Map(group);
        SkipTriage.Endpoint.Map(group);
        Retriage.Endpoint.Map(group);

        return routes;
    }
}
