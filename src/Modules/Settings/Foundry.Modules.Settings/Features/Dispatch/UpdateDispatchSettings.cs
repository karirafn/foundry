using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features.Dispatch;

internal static class UpdateDispatchSettings
{
    internal sealed record Command(bool AutoResumeOnUsageReset, int ProbeIntervalMinutes)
        : ICommand<GlobalSettingsSummary>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        public Result Validate(Command command)
        {
            if (command.ProbeIntervalMinutes < GlobalSettings.MinProbeIntervalMinutes)
            {
                return SettingsErrors.InvalidProbeInterval(command.ProbeIntervalMinutes);
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

            Result probeResult = settings.UpdateProbeInterval(command.ProbeIntervalMinutes);
            if (probeResult is Result.Failure probeFailure)
            {
                return Result<GlobalSettingsSummary>.Fail(probeFailure.Error);
            }

            settings.UpdateDispatchSettings(command.AutoResumeOnUsageReset);

            await dbContext.SaveChangesAsync(cancellationToken);

            return GlobalSettingsMapper.ToSummary(settings);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(bool AutoResumeOnUsageReset, int ProbeIntervalMinutes);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/dispatch", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.AutoResumeOnUsageReset, body.ProbeIntervalMinutes);
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, NotFound, BadRequest<string>>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdateDispatchSettings")
                .WithSummary("Updates dispatch settings including auto-resume and probe interval")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
