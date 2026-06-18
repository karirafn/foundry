using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal static class UpdateDispatchSettings
{
    internal sealed record Command(bool AutoResumeOnUsageReset, int DefaultCooldownMinutes)
        : ICommand<GlobalSettingsSummary>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        public Result Validate(Command command)
        {
            if (command.DefaultCooldownMinutes < GlobalSettings.MinDefaultCooldownMinutes
                || command.DefaultCooldownMinutes > GlobalSettings.MaxDefaultCooldownMinutes)
            {
                return SettingsErrors.InvalidDefaultCooldown(command.DefaultCooldownMinutes);
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

            Result updateResult = settings.UpdateDispatchSettings(
                command.AutoResumeOnUsageReset,
                command.DefaultCooldownMinutes);

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
        private sealed record RequestBody(bool AutoResumeOnUsageReset, int DefaultCooldownMinutes);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/dispatch", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.AutoResumeOnUsageReset, body.DefaultCooldownMinutes);
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, NotFound, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateDispatchSettings")
                .WithSummary("Updates the dispatch pause and cooldown settings")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
