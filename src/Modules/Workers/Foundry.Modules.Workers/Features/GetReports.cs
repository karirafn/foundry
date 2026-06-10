using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features;

internal static class GetReports
{
    internal sealed record Query(WorkerRunId WorkerRunId) : IQuery<GetReportsResponse>;

    internal sealed class Handler(DbContext dbContext)
        : IQueryHandler<Query, GetReportsResponse>
    {
        public async Task<Result<GetReportsResponse>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            WorkerRun? run = await dbContext.Set<WorkerRun>()
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == query.WorkerRunId, cancellationToken);

            if (run is null)
            {
                return Result<GetReportsResponse>.Fail(
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

            string? containerOutput = run is FailedRun failedRun
                ? failedRun.ContainerOutput
                : null;

            return new GetReportsResponse(reports, containerOutput);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/{runId:guid}/reports", static async (
                    Guid runId,
                    IQueryHandler<Query, GetReportsResponse> handler,
                    CancellationToken cancellationToken) =>
                {
                    Query query = new(WorkerRunId.From(runId));
                    Result<GetReportsResponse> result = await handler.HandleAsync(
                        query,
                        cancellationToken);

                    return result.Match<Results<Ok<GetReportsResponse>, NotFound, BadRequest<string>>>(
                        response => TypedResults.Ok(response),
                        error => error.Code switch
                        {
                            WorkerRunErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("GetWorkerReports")
                .WithSummary("Gets historical reports for a worker run ordered by sequence number")
                .Produces<GetReportsResponse>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
