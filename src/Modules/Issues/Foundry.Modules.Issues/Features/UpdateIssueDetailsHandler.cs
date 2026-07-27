using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class UpdateIssueDetailsHandler(
    DbContext db) : IIntegrationEventHandler<IssueDetailsChanged>
{
    public async Task HandleAsync(IssueDetailsChanged @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.MonitoredRepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is null)
        {
            return;
        }

        issue.UpdateDetails(@event.Title, @event.Body, @event.Labels);
        await db.SaveChangesAsync(cancellationToken);
    }
}
