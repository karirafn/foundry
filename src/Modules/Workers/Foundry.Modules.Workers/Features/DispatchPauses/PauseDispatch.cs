using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.DispatchPauses;

internal static class PauseDispatch
{
    internal sealed record Command : ICommand<GlobalSettingsSummary>;

    internal sealed class Handler(DbContext dbContext, IGlobalSettingsQueries settingsQueries)
        : ICommandHandler<Command, GlobalSettingsSummary>
    {
        public Task<Result<GlobalSettingsSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken) =>
            DispatchPauseSummary.ApplyAsync(
                dbContext,
                settingsQueries,
                pause =>
                {
                    pause.PauseDispatch();
                    return Task.CompletedTask;
                },
                cancellationToken);
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/dispatch/pause", static async (
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(new Command(), cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.ToProblem(StatusCodes.Status404NotFound));
                })
                .WithName("PauseDispatch")
                .WithSummary("Pauses worker dispatch")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
