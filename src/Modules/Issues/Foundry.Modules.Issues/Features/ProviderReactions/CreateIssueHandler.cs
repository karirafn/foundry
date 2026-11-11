using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.CreateIssue")]
internal sealed class CreateIssueHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<CreateIssueHandler> logger) : IIntegrationEventHandler<IssueDetected>
{
    public async Task HandleAsync(IssueDetected @event, CancellationToken cancellationToken)
    {
        Result<IssueAuthor> authorResult = IssueAuthor.Create(@event.Author);
        if (authorResult is not Result<IssueAuthor>.Success authorSuccess)
        {
            if (authorResult is Result<IssueAuthor>.Failure authorFailure)
            {
                logger.LogWarning(
                    "Skipping issue #{IssueNumber}: invalid author — {Error}",
                    @event.IssueNumber,
                    authorFailure.Error);
            }

            return;
        }

        Result<ProviderUrl> urlResult = ProviderUrl.Create(@event.Url);
        if (urlResult is not Result<ProviderUrl>.Success urlSuccess)
        {
            if (urlResult is Result<ProviderUrl>.Failure urlFailure)
            {
                logger.LogWarning(
                    "Skipping issue #{IssueNumber}: invalid URL — {Error}",
                    @event.IssueNumber,
                    urlFailure.Error);
            }

            return;
        }

        DetectedIssue detected = DetectedIssue.Detect(
            @event.MonitoredRepositoryId,
            @event.IssueNumber,
            @event.Title,
            authorSuccess.Value,
            urlSuccess.Value,
            @event.Labels,
            @event.DetectedAt,
            KindClassification.FromKindLabel(@event.IssueKindLabel));

        // Every new issue is triaged before its first implement run, and before the dependency check (ADR 0097).
        TriageQueuedIssue triageQueued = detected.QueueTriage();

        db.Set<Issue>().Add(triageQueued);
        await db.SaveChangesAsync(cancellationToken);
        await domainEventDispatcher.DispatchAsync(detected.DomainEvents, cancellationToken);
        detected.ClearDomainEvents();
    }
}
