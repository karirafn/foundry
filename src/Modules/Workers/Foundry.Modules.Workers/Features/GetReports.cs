using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features;

internal static class GetReports
{
    internal sealed record Query(WorkerRunId WorkerRunId) : IQuery<IReadOnlyList<WorkerReportSummary>>;

    internal sealed class Handler(DbContext dbContext)
        : IQueryHandler<Query, IReadOnlyList<WorkerReportSummary>>
    {
        public async Task<Result<IReadOnlyList<WorkerReportSummary>>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            bool runExists = await dbContext.Set<WorkerRun>()
                .AnyAsync(r => r.Id == query.WorkerRunId, cancellationToken);

            if (!runExists)
            {
                return Result<IReadOnlyList<WorkerReportSummary>>.Fail(
                    WorkerRunErrors.NotFound(query.WorkerRunId));
            }

            List<WorkerReportSummary> reports = await dbContext.Set<WorkerReport>()
                .AsNoTracking()
                .Where(r => r.WorkerRunId == query.WorkerRunId)
                .OrderBy(r => r.SequenceNumber)
                .Select(r => new WorkerReportSummary(
                    r.Id.Value,
                    r.WorkerRunId.Value,
                    r.SequenceNumber,
                    r.ReportType,
                    r.Content,
                    r.IngestedAt))
                .ToListAsync(cancellationToken);

            return reports;
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/{runId:guid}/reports", static async (
                    Guid runId,
                    IQueryHandler<Query, IReadOnlyList<WorkerReportSummary>> handler,
                    CancellationToken cancellationToken) =>
                {
                    Query query = new(WorkerRunId.From(runId));
                    Result<IReadOnlyList<WorkerReportSummary>> result = await handler.HandleAsync(
                        query,
                        cancellationToken);

                    return result.Match(
                        reports => TypedResults.Ok(reports) as IResult,
                        error => error.Code switch
                        {
                            WorkerRunErrors.NotFoundCode => TypedResults.NotFound() as IResult,
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("GetWorkerReports")
                .WithSummary("Gets historical reports for a worker run ordered by sequence number")
                .Produces<IReadOnlyList<WorkerReportSummary>>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
