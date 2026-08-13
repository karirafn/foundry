using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.WorkerReactions;

// Concurrency note: SpendState.Blocked is persisted state.
// BlockSpend() is a no-op when already blocked, so duplicate credits-exhausted events are idempotent.
internal sealed class WorkerCreditsExhaustedHandler(
    DbContext dbContext,
    IGlobalSettingsQueries settingsQueries,
    ILogger<WorkerCreditsExhaustedHandler> logger) : IIntegrationEventHandler<WorkerCreditsExhausted>
{
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

        int intervalMinutes = await settingsQueries.GetProbeIntervalMinutesAsync(cancellationToken);
        DateTimeOffset nextProbeAt = DateTimeOffset.UtcNow.AddMinutes(intervalMinutes);
        bool stateChanged = account.BlockSpend(nextProbeAt);

        if (stateChanged)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
