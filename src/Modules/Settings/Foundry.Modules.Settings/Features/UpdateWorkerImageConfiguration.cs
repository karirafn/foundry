using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal static class UpdateWorkerImageConfiguration
{
    internal sealed record Command(
        bool InstallDotnet,
        bool InstallAngular,
        bool InstallGlab,
        bool InstallGh,
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

            WorkerImageConfiguration config = new(
                command.InstallDotnet,
                command.InstallAngular,
                command.InstallGlab,
                command.InstallGh,
                command.InstallChromium,
                command.InstallDocker);

            bool changed = settings.UpdateWorkerImageConfiguration(config);

            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                await integrationEventDispatcher.DispatchAsync(
                    [new WorkerImageConfigurationChanged()],
                    cancellationToken);
            }

            return GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(
            bool InstallDotnet,
            bool InstallAngular,
            bool InstallGlab,
            bool InstallGh,
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
                        body.InstallDotnet,
                        body.InstallAngular,
                        body.InstallGlab,
                        body.InstallGh,
                        body.InstallChromium,
                        body.InstallDocker);

                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, NotFound, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateWorkerImageConfiguration")
                .WithSummary("Updates the worker image build flags")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
