using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.WorkerReactions;

// Concurrency note: SpendState.Blocked is persisted state.
// BlockSpend() is a no-op when already blocked, so duplicate credits-exhausted events are idempotent.
internal sealed class WorkerCreditsExhaustedHandler(
    DbContext dbContext,
    ILogger<WorkerCreditsExhaustedHandler> logger) : IIntegrationEventHandler<WorkerCreditsExhausted>
{
    // TODO(step 3): read ProbeIntervalMinutes from GlobalSettings instead of this default.
    private const int DefaultProbeIntervalMinutes = 60;

    public async Task HandleAsync(WorkerCreditsExhausted @event, CancellationToken cancellationToken)
    {
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            logger.LogWarning(
                "WorkerCreditsExhausted received but no ClaudeAccount row exists; ignoring.");
            return;
        }

        DateTimeOffset nextProbeAt = DateTimeOffset.UtcNow.AddMinutes(DefaultProbeIntervalMinutes);
        bool stateChanged = account.BlockSpend(nextProbeAt);

        if (stateChanged)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
