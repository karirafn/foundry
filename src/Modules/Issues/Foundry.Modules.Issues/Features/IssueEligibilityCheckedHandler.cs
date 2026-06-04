using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class IssueEligibilityCheckedHandler(
    DbContext db,
    IDomainEventDispatcher dispatcher,
    ILogger<IssueEligibilityCheckedHandler> logger) : IIntegrationEventHandler<IssueEligibilityChecked>
{
    public async Task HandleAsync(IssueEligibilityChecked @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.MonitoredRepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is null)
        {
            logger.LogWarning(
                "Issue {IssueNumber} not found for repository {RepositoryId} during eligibility check",
                @event.IssueNumber,
                @event.MonitoredRepositoryId);
            return;
        }

        List<EligibilityViolation> violations = @event.Violations
            .Select(v => EligibilityViolation.From(v.Rule, v.Description))
            .ToList();

        switch (issue)
        {
            case DetectedIssue detected when violations.Count > 0:
            {
                IneligibleIssue ineligible = detected.MarkIneligible(violations);
                await db.TransitionAsync(detected, ineligible, dispatcher, cancellationToken);
                break;
            }

            case IneligibleIssue ineligible when violations.Count > 0:
            {
                Result updateResult = ineligible.UpdateViolations(violations);
                if (!updateResult.IsSuccess)
                {
                    logger.LogWarning(
                        "Failed to update violations for issue {IssueId}: {Error}",
                        ineligible.Id,
                        ((Result.Failure)updateResult).Error.Message);
                    break;
                }

                await db.SaveChangesAsync(cancellationToken);
                break;
            }

            case IneligibleIssue ineligible when violations.Count == 0:
            {
                Issue next = ineligible.ClearViolations();
                switch (next)
                {
                    case QueuedIssue queued:
                        await db.TransitionAsync(ineligible, queued, dispatcher, cancellationToken);
                        break;
                    case BlockedIssue blocked:
                        await db.TransitionAsync(ineligible, blocked, dispatcher, cancellationToken);
                        break;
                }

                break;
            }

            // Behavior (e): DetectedIssue with no violations — do nothing.
            // The dependencies handler owns the DetectedIssue → QueuedIssue transition.
        }
    }
}
