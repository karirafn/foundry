using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.DispatchPauses;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.CredentialsReactions;

/// <summary>
/// Pauses dispatch until the usage limit the Credentials credit probe found resets, as
/// <see cref="Outcome.WorkerOutcomeApplier"/> does for a run that hit the limit. The reset time only ever extends, so a
/// redelivery leaves it unchanged and broadcasts nothing.
/// </summary>
[IntegrationEventHandlerIdentity("Workers.UsageLimitReached")]
internal sealed class UsageLimitReachedHandler(
    DbContext dbContext,
    IIntegrationEventProcessor integrationEventProcessor,
    ILogger<UsageLimitReachedHandler> logger) : IIntegrationEventHandler<UsageLimitReached>
{
    public async Task HandleAsync(UsageLimitReached @event, CancellationToken cancellationToken)
    {
        DispatchPause? pause = await dbContext.FindDispatchPauseAsync(cancellationToken);

        if (pause is null)
        {
            logger.LogWarning(
                "Usage limit reached but could not persist reset time — no dispatch pause row exists.");
            return;
        }

        DateTimeOffset? resetsAtBefore = pause.UsageLimitResetsAt;
        pause.SetUsageLimitResetsAt(@event.ResetsAt);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (pause.UsageLimitResetsAt == resetsAtBefore)
        {
            return;
        }

        // DispatchPaused has only a SignalR broadcast handler, so it is delivered directly rather than through the outbox.
        await TryDeliverDirectAsync(new DispatchPaused(pause.UsageLimitResetsAt!.Value), cancellationToken);

        logger.LogWarning(
            "Usage limit reached; dispatch paused until {ResetsAt}.",
            pause.UsageLimitResetsAt);
    }

    private async Task TryDeliverDirectAsync(DispatchPaused paused, CancellationToken cancellationToken)
    {
        try
        {
            await integrationEventProcessor.ProcessAsync(Guid.NewGuid(), paused, cancellationToken);
        }
#pragma warning disable CA1031 // A failed broadcast must not fail the handler: redelivery would find the reset time unchanged and never broadcast again.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to deliver {EventType} directly.", nameof(DispatchPaused));
        }
    }
}
