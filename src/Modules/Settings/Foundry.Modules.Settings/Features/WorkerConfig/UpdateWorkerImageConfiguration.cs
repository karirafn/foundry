using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features.WorkerConfig;

internal static class UpdateWorkerImageConfiguration
{
    internal sealed record Command(
        string ImageTag,
        bool BuildEnabled) : ICommand<GlobalSettingsSummary>;

    internal sealed class Handler(
        DbContext dbContext,
        IWebhookIngressProbe ingressProbe,
        IDispatchPauseQueries dispatchPauseQueries,
        IIntegrationEventDispatcher integrationEventDispatcher)
        : ICommandHandler<Command, GlobalSettingsSummary>
    {
        public async Task<Result<GlobalSettingsSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            GlobalSettings? settings = await dbContext.FindGlobalSettingsAsync(cancellationToken);

            if (settings is null)
            {
                return Result<GlobalSettingsSummary>.Fail(SettingsErrors.NotFound);
            }

            Result<WorkerImageConfiguration> configResult = WorkerImageConfiguration.Create(
                imageTag: command.ImageTag,
                buildEnabled: command.BuildEnabled);

            if (configResult is not Result<WorkerImageConfiguration>.Success configSuccess)
            {
                return Result<GlobalSettingsSummary>.Fail(((Result<WorkerImageConfiguration>.Failure)configResult).Error);
            }

            WorkerImageConfiguration config = configSuccess.Value;
            bool changed = settings.UpdateWorkerImageConfiguration(config);

            if (changed)
            {
                await integrationEventDispatcher.DispatchAsync(
                    [new WorkerImageSettingsChanged()],
                    cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return await GlobalSettingsMapper.ToSummaryAsync(settings, dispatchPauseQueries, ingressProbe.Port, cancellationToken);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(
            string ImageTag,
            bool BuildEnabled);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/worker-image", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.ImageTag, body.BuildEnabled);

                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateWorkerImageConfiguration")
                .WithSummary("Updates the worker image tag and build toggle")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
