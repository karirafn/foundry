using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.PullRequestMarkedReady")]
internal sealed class PullRequestMarkedReadyHandler(DbContext db) : IIntegrationEventHandler<PullRequestMarkedReady>
{
    public async Task HandleAsync(PullRequestMarkedReady @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is null || !issue.RecordPullRequestMarkedReady(@event.PullRequestUrl))
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
