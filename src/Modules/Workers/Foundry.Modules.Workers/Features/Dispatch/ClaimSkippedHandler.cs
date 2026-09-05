using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Releases the <see cref="DispatchReservation"/> held for an authorization that produced
/// no claim (the Issues module published <see cref="ClaimSkipped"/>).
/// Idempotent: when no reservation exists the handler is a no-op, covering redelivery
/// and the case where the sweep already released it.
/// </summary>
internal sealed class ClaimSkippedHandler(
    DbContext dbContext,
    ILogger<ClaimSkippedHandler> logger) : IIntegrationEventHandler<ClaimSkipped>
{
    public async Task HandleAsync(ClaimSkipped @event, CancellationToken cancellationToken)
    {
        DispatchReservation? reservation = await dbContext.Set<DispatchReservation>()
            .FindAsync([@event.WorkerRunId], cancellationToken);

        if (reservation is null)
        {
            logger.LogDebug(
                "No reservation found for worker run {WorkerRunId}; ClaimSkipped is a no-op.",
                @event.WorkerRunId);
            return;
        }

        dbContext.Set<DispatchReservation>().Remove(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "Released dispatch reservation for worker run {WorkerRunId} after ClaimSkipped.",
            @event.WorkerRunId);
    }
}
