using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.AwaitingInput;

/// <summary>
/// Closes the open clarification round with the input that arrived and resumes the work along the issue's resume
/// target (ADR 0096). Redelivery is a no-op: the issue has already left awaiting input.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.AwaitingInputArrived")]
internal sealed class AwaitingInputArrivedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<AwaitingInputArrivedHandler> logger) : IIntegrationEventHandler<AwaitingInputArrived>
{
    public async Task HandleAsync(AwaitingInputArrived @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        // Input that arrived no later than the question was posted belongs to an earlier round.
        if (issue is not AwaitingInputIssue { QuestionPost: QuestionPost.Posted posted } awaiting
            || @event.ArrivedAt <= posted.PostedAt)
        {
            logger.LogWarning(
                "AwaitingInputArrived received for repository {RepositoryId} issue {IssueNumber} but it is not awaiting input on a question posted before it (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        QueuedIssue queued = awaiting.ProvideInput(@event.Input, @event.ArrivedAt, timeProvider.GetUtcNow());
        await db.TransitionAsync(awaiting, queued, domainEventDispatcher, cancellationToken);
    }
}
