using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class WorkerRunFailedHandler(
    DbContext db,
    ILogger<WorkerRunFailedHandler> logger) : IIntegrationEventHandler<WorkerRunFailed>
{
    public async Task HandleAsync(WorkerRunFailed @event, CancellationToken cancellationToken)
    {
        IssueId issueId = IssueId.From(@event.IssueId);

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is InProgressIssue inProgress)
        {
            FailedIssue failed = inProgress.MarkFailed(
                @event.WorkerRunId,
                @event.ReasonDescription,
                DateTimeOffset.UtcNow);
            await db.TransitionAsync(inProgress, failed, cancellationToken);
            return;
        }

        if (issue is RevisionInProgressIssue revisionInProgress)
        {
            RevisionFailedIssue revisionFailed = revisionInProgress.MarkFailed(
                @event.WorkerRunId,
                @event.ReasonDescription,
                DateTimeOffset.UtcNow);
            await db.TransitionAsync(revisionInProgress, revisionFailed, cancellationToken);
            return;
        }

        logger.LogWarning(
            "WorkerRunFailed received for issue {IssueId} but it is not InProgress (state: {State}); ignoring.",
            @event.IssueId,
            issue?.GetType().Name ?? "not found");
    }
}
