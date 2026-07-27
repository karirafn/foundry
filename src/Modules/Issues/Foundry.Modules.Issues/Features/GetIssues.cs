using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
                    string[]? states,
                    string? cursor,
                    int? limit,
                    IIssueQueries queries,
                    CancellationToken cancellationToken) =>
                {
                    MonitoredRepositoryId? repoId = repositoryId.HasValue
                        ? MonitoredRepositoryId.From(repositoryId.Value)
                        : null;

                    List<string> normalizedStates = NormalizeStates(states);

                    if (normalizedStates.Count > 0)
                    {
                        List<string> unknownNames = normalizedStates
                            .Where(s => !IssueStateRegistry.IsKnownName(s))
                            .ToList();

                        if (unknownNames.Count > 0)
                        {
                            return (IResult)TypedResults.Problem(
                                detail: IssueErrors.InvalidStates(unknownNames).Message,
                                statusCode: StatusCodes.Status400BadRequest);
                        }

                        bool hasActive = normalizedStates.Any(IssueStateRegistry.Active.Contains);
                        bool hasResolved = normalizedStates.Any(IssueStateRegistry.Resolved.Contains);

                        if (hasActive && hasResolved)
                        {
                            return (IResult)TypedResults.Problem(
                                detail: IssueErrors.MixedStates().Message,
                                statusCode: StatusCodes.Status400BadRequest);
                        }

                        if (hasResolved)
                        {
                            return await HandleResolvedAsync(
                                repoId,
                                normalizedStates,
                                cursor,
                                limit,
                                queries,
                                cancellationToken);
                        }
                    }

                    IReadOnlyList<IssueSummary> summaries = await queries.GetActiveIssueSummariesAsync(
                        repoId,
                        normalizedStates.Count > 0 ? normalizedStates : null,
                        cancellationToken);

                    return TypedResults.Ok(summaries) as IResult;
                })
                .WithName("GetIssues")
                .WithSummary("Gets issue summaries, optionally filtered by repository and state")
                .Produces<IReadOnlyList<IssueSummary>>()
                .Produces<PagedIssues>()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }

        private static async Task<IResult> HandleResolvedAsync(
            MonitoredRepositoryId? repoId,
            List<string> states,
            string? cursor,
            int? limit,
            IIssueQueries queries,
            CancellationToken cancellationToken)
        {
            if (cursor is not null)
            {
                Result<(DateTimeOffset DetectedAt, IssueId Id)> decoded = IssueCursor.Decode(cursor);
                if (decoded.IsFailure)
                {
                    return TypedResults.Problem(
                        detail: IssueErrors.InvalidCursor().Message,
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }

            PagedIssues paged = await queries.GetResolvedIssueSummariesAsync(
                repoId,
                states,
                cursor,
                limit ?? 0,
                cancellationToken);

            return TypedResults.Ok(paged);
        }

        private const int MaxStateTokenLength = 64;
        private const int MaxStateTokenCount = 50;

        private static List<string> NormalizeStates(string[]? states)
        {
            if (states is null || states.Length == 0)
            {
                return [];
            }

            return states
                .Take(MaxStateTokenCount)
                .SelectMany(s => s.Split(','))
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length > 0 && s.Length <= MaxStateTokenLength)
                .Take(MaxStateTokenCount)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }
}
