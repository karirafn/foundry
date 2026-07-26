using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features;

// Concurrency note: validity is persisted state, login-in-progress is transient.
// Invalidate() is a no-op when already invalid, so duplicate auth-fail events are idempotent.
// A later successful login authoritatively sets Valid (last-writer-wins), so the ordering of
// an auth-invalid event racing an active login is safe — the login's RecordSuccessfulLogin wins.
internal sealed class WorkerAuthenticationFailedHandler(
    DbContext dbContext,
    IIntegrationEventDispatcher integrationEventDispatcher,
    ILogger<WorkerAuthenticationFailedHandler> logger) : IIntegrationEventHandler<WorkerAuthenticationFailed>
{
    public async Task HandleAsync(WorkerAuthenticationFailed @event, CancellationToken cancellationToken)
    {
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            logger.LogWarning(
                "WorkerAuthenticationFailed received but no ClaudeAccount row exists; ignoring.");
            return;
        }

        bool stateChanged = account.Invalidate(@event.Reason);

        if (stateChanged)
        {
            await integrationEventDispatcher.DispatchAsync(
                [new CredentialsInvalidated(@event.Reason)],
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
