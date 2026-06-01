using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features;

internal static class IngestReport
{
    internal sealed record Command(
        WorkerRunId WorkerRunId,
        string ReportType,
        string Content) : ICommand<WorkerReportSummary>;

    internal sealed class Handler(
        DbContext dbContext,
        IWorkerLogBroadcaster broadcaster) : ICommandHandler<Command, WorkerReportSummary>
    {
        public async Task<Result<WorkerReportSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            WorkerRun? run = await dbContext.Set<WorkerRun>()
                .FirstOrDefaultAsync(r => r.Id == command.WorkerRunId, cancellationToken);

            if (run is null)
            {
                return Result<WorkerReportSummary>.Fail(WorkerRunErrors.NotFound(command.WorkerRunId));
            }

            if (run is not ActiveRun)
            {
                return Result<WorkerReportSummary>.Fail(WorkerRunErrors.NotActive(command.WorkerRunId));
            }

            int maxSequence = await dbContext.Set<WorkerReport>()
                .Where(r => r.WorkerRunId == command.WorkerRunId)
                .Select(r => (int?)r.SequenceNumber)
                .MaxAsync(cancellationToken) ?? 0;

            WorkerReport report = WorkerReport.Create(
                command.WorkerRunId,
                maxSequence + 1,
                command.ReportType,
                command.Content);

            dbContext.Set<WorkerReport>().Add(report);
            await dbContext.SaveChangesAsync(cancellationToken);

            WorkerReportSummary summary = new(
                report.Id.Value,
                report.WorkerRunId.Value,
                report.SequenceNumber,
                report.ReportType,
                report.Content,
                report.IngestedAt);

            await broadcaster.PushAsync(run.IssueId.Value, summary, cancellationToken);

            return summary;
        }
    }

    internal static class Endpoint
    {
        internal sealed record Request(string Type, string Content);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/{runId:guid}/reports", static async (
                    Guid runId,
                    Request request,
                    ICommandHandler<Command, WorkerReportSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(
                        WorkerRunId.From(runId),
                        request.Type,
                        request.Content);

                    Result<WorkerReportSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match(
                        summary => TypedResults.Created($"/api/workers/{runId}/reports/{summary.Id}", summary) as IResult,
                        error => error.Code switch
                        {
                            WorkerRunErrors.NotFoundCode => TypedResults.NotFound() as IResult,
                            WorkerRunErrors.NotActiveCode => TypedResults.BadRequest(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("IngestWorkerReport")
                .WithSummary("Ingests a progress report from a worker run")
                .Produces<WorkerReportSummary>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
