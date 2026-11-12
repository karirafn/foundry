using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

[IntegrationEventHandlerIdentity("Issues.WorkerRunFailed")]
internal sealed class WorkerRunFailedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<WorkerRunFailedHandler> logger) : IIntegrationEventHandler<WorkerRunFailed>
{
    public async Task HandleAsync(WorkerRunFailed @event, CancellationToken cancellationToken)
    {
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
        IssueId issueId = IssueId.From(@event.IssueId);

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        // Convert the string? category from the contract to the typed FailureCategory at the domain boundary.
        // Unknown or missing tokens fall back to NonZeroExit (the neutral generic terminal) and are logged.
        FailureCategory category;
        if (@event.Category is { } token && FailureCategory.Create(token) is Result<FailureCategory>.Success ok)
        {
            category = ok.Value;
        }
        else
        {
            string? sanitizedCategory = @event.Category
                ?.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            logger.LogWarning(
                "WorkerRunFailed for issue {IssueId} carried an unrecognised or missing category '{Category}'; defaulting to {Fallback}.",
                @event.IssueId,
                sanitizedCategory,
                FailureCategory.NonZeroExitToken);
            category = FailureCategory.NonZeroExit;
        }

        if (issue is InProgressIssue inProgress)
        {
            if (@event.WorkerRunId != inProgress.WorkerRunId)
            {
                logger.LogWarning(
                    "WorkerRunFailed for issue {IssueId} has run id {EventRunId} which does not match current run id {CurrentRunId}; ignoring stale event.",
                    @event.IssueId,
                    @event.WorkerRunId,
                    inProgress.WorkerRunId);
                return;
            }

            // An exhausted publish leaves the run's work unpublished in its Hand-off volume, so the issue waits for
            // Retry publish rather than failing: Retry Issue would start a new run and abandon that work (ADR 0100).
            if (category == FailureCategory.PublishFailed)
            {
                if (@event.BranchName is null)
                {
                    logger.LogError(
                        "WorkerRunFailed for issue {IssueId} reports a failed publish but no BranchName; skipping transition.",
                        @event.IssueId);
                    return;
                }

                PublishFailedIssue publishFailed = inProgress.MarkPublishFailed(
                    @event.BranchName,
                    @event.ReasonDescription,
                    failedAt);
                await db.TransitionAsync(inProgress, publishFailed, domainEventDispatcher, cancellationToken);
                return;
            }

            if (@event.BranchName is not null)
            {
                ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
                    @event.BranchName,
                    @event.ReasonDescription,
                    category,
                    failedAt);
                await db.TransitionAsync(inProgress, continuableFailed, domainEventDispatcher, cancellationToken);
            }
            else
            {
                FailedIssue failed = inProgress.MarkFailed(
                    @event.ReasonDescription,
                    failedAt,
                    category);
                await db.TransitionAsync(inProgress, failed, domainEventDispatcher, cancellationToken);
            }
            return;
        }

        // Revision failures always become RevisionFailedIssue regardless of branch presence —
        // the revision path has its own retry mechanism (RevisionFailedIssue.Retry → RevisionQueuedIssue).
        if (issue is RevisionInProgressIssue revisionInProgress)
        {
            if (@event.WorkerRunId != revisionInProgress.WorkerRunId)
            {
                logger.LogWarning(
                    "WorkerRunFailed for issue {IssueId} has run id {EventRunId} which does not match current run id {CurrentRunId}; ignoring stale event.",
                    @event.IssueId,
                    @event.WorkerRunId,
                    revisionInProgress.WorkerRunId);
                return;
            }

            if (category == FailureCategory.PublishFailed)
            {
                PublishFailedIssue revisionPublishFailed = revisionInProgress.MarkPublishFailed(
                    @event.ReasonDescription,
                    failedAt);
                await db.TransitionAsync(revisionInProgress, revisionPublishFailed, domainEventDispatcher, cancellationToken);
                return;
            }

            RevisionFailedIssue revisionFailed = revisionInProgress.MarkFailed(
                @event.ReasonDescription,
                category,
                failedAt);
            await db.TransitionAsync(revisionInProgress, revisionFailed, domainEventDispatcher, cancellationToken);
            return;
        }

        // The run waiting to publish ended without publishing (its time to live discarded the work); a repeated
        // publish_failed for the same run changes nothing.
        if (issue is PublishFailedIssue publishFailedIssue)
        {
            if (@event.WorkerRunId != publishFailedIssue.WorkerRunId || category == FailureCategory.PublishFailed)
            {
                logger.LogWarning(
                    "WorkerRunFailed for issue {IssueId} with run id {EventRunId} and category {Category} does not end its unpublished run {CurrentRunId}; ignoring.",
                    @event.IssueId,
                    @event.WorkerRunId,
                    category.Value,
                    publishFailedIssue.WorkerRunId);
                return;
            }

            Issue failed = publishFailedIssue.Fail(@event.ReasonDescription, category, failedAt);
            await db.TransitionAsync(publishFailedIssue, failed, domainEventDispatcher, cancellationToken);
            return;
        }

        // The run whose publish the Diff gate blocked ended without publishing: an operator discarded it, or its time to
        // live did (#788).
        if (issue is PublishBlockedIssue publishBlockedIssue)
        {
            if (@event.WorkerRunId != publishBlockedIssue.WorkerRunId)
            {
                logger.LogWarning(
                    "WorkerRunFailed for issue {IssueId} has run id {EventRunId} which does not match its blocked run {CurrentRunId}; ignoring.",
                    @event.IssueId,
                    @event.WorkerRunId,
                    publishBlockedIssue.WorkerRunId);
                return;
            }

            Issue discarded = publishBlockedIssue.Fail(@event.ReasonDescription, category, failedAt);
            await db.TransitionAsync(publishBlockedIssue, discarded, domainEventDispatcher, cancellationToken);
            return;
        }

        // A failed triage run never falls back to facts (ADR 0094): the issue waits in triage failed.
        if (issue is TriageInProgressIssue triaging)
        {
            if (@event.WorkerRunId != triaging.WorkerRunId)
            {
                logger.LogWarning(
                    "WorkerRunFailed for issue {IssueId} has run id {EventRunId} which does not match current run id {CurrentRunId}; ignoring stale event.",
                    @event.IssueId,
                    @event.WorkerRunId,
                    triaging.WorkerRunId);
                return;
            }

            TriageFailedIssue triageFailed = triaging.MarkFailed(@event.ReasonDescription, category, failedAt);
            await db.TransitionAsync(triaging, triageFailed, domainEventDispatcher, cancellationToken);
            return;
        }

        logger.LogWarning(
            "WorkerRunFailed received for issue {IssueId} but it is not InProgress (state: {State}); ignoring.",
            @event.IssueId,
            issue?.GetType().Name ?? "not found");
    }
}
