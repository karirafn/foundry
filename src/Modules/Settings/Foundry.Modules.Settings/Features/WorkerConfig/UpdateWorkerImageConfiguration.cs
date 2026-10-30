using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
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
        bool BuildEnabled,
        bool InstallDotnet,
        bool InstallAngular,
        bool InstallChromium,
        bool InstallDocker) : ICommand<GlobalSettingsSummary>;

    internal sealed class Handler(
        DbContext dbContext,
        IIntegrationEventDispatcher integrationEventDispatcher)
        : ICommandHandler<Command, GlobalSettingsSummary>
    {
        public async Task<Result<GlobalSettingsSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is null)
            {
                return Result<GlobalSettingsSummary>.Fail(SettingsErrors.NotFound);
            }

            Result<WorkerImageConfiguration> configResult = WorkerImageConfiguration.Create(
                imageTag: command.ImageTag,
                buildEnabled: command.BuildEnabled,
                installDotnet: command.InstallDotnet,
                installAngular: command.InstallAngular,
                installChromium: command.InstallChromium,
                installDocker: command.InstallDocker);

            if (configResult is not Result<WorkerImageConfiguration>.Success configSuccess)
            {
                return Result<GlobalSettingsSummary>.Fail(((Result<WorkerImageConfiguration>.Failure)configResult).Error);
            }

            WorkerImageConfiguration config = configSuccess.Value;
            bool changed = settings.UpdateWorkerImageConfiguration(config);

            if (changed)
            {
                await integrationEventDispatcher.DispatchAsync(
                    [new WorkerImageConfigurationChanged()],
                    cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return GlobalSettingsMapper.ToSummary(settings);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(
            string ImageTag,
            bool BuildEnabled,
            bool InstallDotnet,
            bool InstallAngular,
            bool InstallChromium,
            bool InstallDocker);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/worker-image", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(
                        body.ImageTag,
                        body.BuildEnabled,
                        body.InstallDotnet,
                        body.InstallAngular,
                        body.InstallChromium,
                        body.InstallDocker);

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
                .WithSummary("Updates the worker image tag, build toggle, and build flags")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
