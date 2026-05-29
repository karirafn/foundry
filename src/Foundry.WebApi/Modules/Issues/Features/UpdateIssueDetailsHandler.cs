using Foundry.Modules.Monitoring.Contracts;
using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Foundry.WebApi.Modules.Issues.Features;

internal sealed class UpdateIssueDetailsHandler(FoundryDbContext db) : IIntegrationEventHandler<IssueDetailsChanged>
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
