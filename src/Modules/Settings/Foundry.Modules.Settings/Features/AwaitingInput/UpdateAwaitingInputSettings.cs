using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features.AwaitingInput;

/// <summary>
/// Saves the awaiting-input reminder delay (ADR 0096): how long a posted question waits without input before Foundry
/// posts its one reminder. A delay that is not a positive number of hours fails with
/// <c>Settings.InvalidAwaitingInputReminderDelay</c>.
/// </summary>
internal static class UpdateAwaitingInputSettings
{
    internal sealed record Command(int ReminderDelayHours) : ICommand<GlobalSettingsSummary>;

    internal sealed class Handler(
        DbContext dbContext,
        IWebhookIngressProbe ingressProbe,
        IDispatchPauseQueries dispatchPauseQueries)
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

            if (settings.UpdateAwaitingInputReminderDelay(command.ReminderDelayHours) is Result.Failure failure)
            {
                return Result<GlobalSettingsSummary>.Fail(failure.Error);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return await GlobalSettingsMapper.ToSummaryAsync(settings, dispatchPauseQueries, ingressProbe.Port, cancellationToken);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(int AwaitingInputReminderDelayHours);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/awaiting-input", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(
                        new Command(body.AwaitingInputReminderDelayHours),
                        cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateAwaitingInputSettings")
                .WithSummary("Updates how long a posted question waits without input before its one reminder")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
