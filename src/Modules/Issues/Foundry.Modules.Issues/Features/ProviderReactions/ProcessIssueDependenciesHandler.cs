using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features.Dependencies;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.ProcessIssueDependencies")]
internal sealed class ProcessIssueDependenciesHandler(
    DbContext db,
    IssueDependencyApplier dependencyApplier,
    ILogger<ProcessIssueDependenciesHandler> logger) : IIntegrationEventHandler<IssueDependenciesDetected>
{
    public async Task HandleAsync(IssueDependenciesDetected @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.MonitoredRepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is null)
        {
            logger.LogWarning(
                "Issue {IssueNumber} not found for repository {RepositoryId} during dependency processing",
                @event.IssueNumber,
                @event.MonitoredRepositoryId);
            return;
        }

        await dependencyApplier.ApplyAsync(issue, @event.BlockedByIssueNumbers, cancellationToken);
    }
}
