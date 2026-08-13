using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.DispatchReactions;

internal sealed class DispatchResumedSpendHandler(
    DbContext dbContext,
    IIntegrationEventDispatcher integrationEventDispatcher,
    ILogger<DispatchResumedSpendHandler> logger) : IIntegrationEventHandler<DispatchResumed>
{
    public async Task HandleAsync(DispatchResumed @event, CancellationToken cancellationToken)
    {
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            logger.LogWarning(
                "DispatchResumed received but no ClaudeAccount row exists; ignoring.");
            return;
        }

        bool stateChanged = account.RestoreSpend();

        if (stateChanged)
        {
            await integrationEventDispatcher.DispatchAsync(
                [new CreditsRestored()],
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
