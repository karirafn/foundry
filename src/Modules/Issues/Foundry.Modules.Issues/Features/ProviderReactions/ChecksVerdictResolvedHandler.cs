using System.Diagnostics;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.ChecksVerdictResolved")]
internal sealed class ChecksVerdictResolvedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<ChecksVerdictResolvedHandler> logger) : IIntegrationEventHandler<ChecksVerdictResolved>
{
    public async Task HandleAsync(ChecksVerdictResolved @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not ChecksPendingIssue checksPendingIssue)
        {
            logger.LogWarning(
                "ChecksVerdictResolved received for repository {RepositoryId} issue {IssueNumber} but it is not a ChecksPendingIssue (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        ChecksOutcome outcome = @event.Outcome switch
        {
            "passed" => new ChecksOutcome.Passed(),
            "none_detected" => new ChecksOutcome.NoneDetected(),
            "unfinished" => new ChecksOutcome.Unfinished(),
            "still_failing" => new ChecksOutcome.StillFailing(@event.FailingChecks),
            "unreadable" => new ChecksOutcome.Unreadable(),
            _ => throw new UnreachableException(
                $"Unknown ChecksOutcome discriminator '{@event.Outcome}' on ChecksVerdictResolved for repository {@event.RepositoryId} issue {@event.IssueNumber}."),
        };

        ReviewIssue reviewIssue = checksPendingIssue.ToReview(outcome);
        await db.TransitionAsync(checksPendingIssue, reviewIssue, domainEventDispatcher, cancellationToken);
    }
}
