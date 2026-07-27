using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal static class UpdateWorkerLimits
{
    internal sealed record Command(int MaxConcurrent, int TimeoutMinutes) : ICommand<GlobalSettingsSummary>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        public Result Validate(Command command)
        {
            if (command.MaxConcurrent < GlobalSettings.MinMaxConcurrent
                || command.MaxConcurrent > GlobalSettings.MaxMaxConcurrent)
            {
                return SettingsErrors.InvalidMaxConcurrent(command.MaxConcurrent);
            }

            if (command.TimeoutMinutes < GlobalSettings.MinTimeoutMinutes
                || command.TimeoutMinutes > GlobalSettings.MaxTimeoutMinutes)
            {
                return SettingsErrors.InvalidTimeout(command.TimeoutMinutes);
            }

            return Result.Ok();
        }
    }

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, GlobalSettingsSummary>
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

            Result updateResult = settings.UpdateLimits(command.MaxConcurrent, command.TimeoutMinutes);
            if (updateResult is Result.Failure failure)
            {
                return Result<GlobalSettingsSummary>.Fail(failure.Error);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return GlobalSettingsMapper.ToSummary(settings);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(int MaxConcurrent, int TimeoutMinutes);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/limits", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.MaxConcurrent, body.TimeoutMinutes);
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, NotFound, BadRequest<string>>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdateWorkerLimits")
                .WithSummary("Updates the worker concurrency and timeout limits")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
