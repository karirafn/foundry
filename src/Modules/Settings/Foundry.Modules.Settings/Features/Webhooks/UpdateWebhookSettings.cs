using Foundry.Modules.Monitoring.Contracts;
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

namespace Foundry.Modules.Settings.Features.Webhooks;

/// <summary>
/// Updates the webhook ingress settings (ADR 0085): the Webhook Public Base URL and the
/// Backstop Interval. A changed URL is saved only once the self-probe reaches this Foundry through it
/// (ADR 0116). Failures carry a field-keyed error code
/// (<c>Settings.InvalidWebhookPublicBaseUrl</c>, <c>Settings.WebhookPublicBaseUrlUnreachable</c>,
/// <c>Settings.InvalidBackstopInterval</c>).
/// </summary>
internal static class UpdateWebhookSettings
{
    internal sealed record Command(string? PublicBaseUrl, int BackstopIntervalMinutes)
        : ICommand<GlobalSettingsSummary>;

    internal sealed class Handler(
        DbContext dbContext,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IWebhookIngressProbe ingressProbe)
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

            Result<WebhookPublicBaseUrl> validated =
                GlobalSettings.ValidateWebhookSettings(command.PublicBaseUrl, command.BackstopIntervalMinutes);
            if (validated is Result<WebhookPublicBaseUrl>.Failure invalid)
            {
                return Result<GlobalSettingsSummary>.Fail(invalid.Error);
            }

            WebhookPublicBaseUrl publicBaseUrl = ((Result<WebhookPublicBaseUrl>.Success)validated).Value;
            string? previousPublicBaseUrl = settings.WebhookPublicBaseUrl?.Value;
            bool urlChanged = !string.Equals(previousPublicBaseUrl, publicBaseUrl.Value, StringComparison.Ordinal);

            // Only a changed URL is probed (ADR 0116), so saving the backstop interval never needs the tunnel.
            if (urlChanged
                && await ingressProbe.VerifyAsync(publicBaseUrl.Value, cancellationToken)
                    is WebhookProbeResult.Unreachable unreachable)
            {
                return Result<GlobalSettingsSummary>.Fail(
                    SettingsErrors.WebhookPublicBaseUrlUnreachable(unreachable.Failure, publicBaseUrl, ingressProbe.Port));
            }

            Result updateResult = settings.UpdateWebhookSettings(command.PublicBaseUrl, command.BackstopIntervalMinutes);
            if (updateResult is Result.Failure failure)
            {
                return Result<GlobalSettingsSummary>.Fail(failure.Error);
            }

            // A new URL re-points every registered hook; the event commits with the change.
            if (urlChanged)
            {
                await integrationEventDispatcher.DispatchAsync(
                    [new WebhookPublicBaseUrlChanged(previousPublicBaseUrl, publicBaseUrl.Value)],
                    cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return GlobalSettingsMapper.ToSummary(settings, ingressProbe.Port);
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
