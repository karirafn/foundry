using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features.Webhooks;

/// <summary>
/// Updates the webhook ingress settings (ADR 0085): the Webhook Public Base URL and the
/// Backstop Interval. Validation failures carry a field-keyed error code
/// (<c>Settings.InvalidWebhookPublicBaseUrl</c>, <c>Settings.InvalidBackstopInterval</c>).
/// </summary>
internal static class UpdateWebhookSettings
{
    internal sealed record Command(string? PublicBaseUrl, int BackstopIntervalMinutes)
        : ICommand<GlobalSettingsSummary>;

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

            string? previousPublicBaseUrl = settings.WebhookPublicBaseUrl;

            Result updateResult = settings.UpdateWebhookSettings(command.PublicBaseUrl, command.BackstopIntervalMinutes);
            if (updateResult is Result.Failure failure)
            {
                return Result<GlobalSettingsSummary>.Fail(failure.Error);
            }

            // A new, non-empty URL re-points every registered hook; the event commits with the change.
            if (settings.WebhookPublicBaseUrl is { } publicBaseUrl
                && !string.Equals(previousPublicBaseUrl, publicBaseUrl, StringComparison.Ordinal))
            {
                await integrationEventDispatcher.DispatchAsync(
                    [new WebhookPublicBaseUrlChanged(previousPublicBaseUrl, publicBaseUrl)],
                    cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return GlobalSettingsMapper.ToSummary(settings);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string? WebhookPublicBaseUrl, int BackstopIntervalMinutes);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/webhooks", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.WebhookPublicBaseUrl, body.BackstopIntervalMinutes);
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateWebhookSettings")
                .WithSummary("Updates the webhook public base URL and the backstop poll interval")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
