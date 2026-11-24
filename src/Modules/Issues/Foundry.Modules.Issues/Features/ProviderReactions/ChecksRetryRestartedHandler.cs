using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.ChecksRetryRestarted")]
internal sealed class ChecksRetryRestartedHandler(DbContext db) : IIntegrationEventHandler<ChecksRetryRestarted>
{
    public async Task HandleAsync(ChecksRetryRestarted @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not ChecksPendingIssue checksPending || !checksPending.RecordChecksRestarted())
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
