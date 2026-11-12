using System.Diagnostics;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

/// <summary>
/// Fails triage when the provider refused to link a blocker triage named. The event is delivered after the fact, so
/// it is acted on only while the issue still waits on the blockers that run named; an issue that has since been
/// triaged again, or that waits on blockers found before triage, no longer has a link to refuse and stays as it is.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.BlockerLinkFailed")]
internal sealed class BlockerLinkFailedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<BlockerLinkFailedHandler> logger) : IIntegrationEventHandler<BlockerLinkFailed>
{
    public async Task HandleAsync(BlockerLinkFailed @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.MonitoredRepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not TriageBlockedIssue blocked)
        {
            logger.LogInformation(
                "BlockerLinkFailed received for repository {RepositoryId} issue {IssueNumber} but it is not a TriageBlockedIssue (state: {State}); ignoring.",
                @event.MonitoredRepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        WorkerRunId workerRunId = WorkerRunId.From(@event.WorkerRunId);

        LinkRefusalMatch match = blocked.MatchRefusedLink(workerRunId, @event.BlockerNumber);

        if (match != LinkRefusalMatch.Matches)
        {
            logger.LogInformation(
                "BlockerLinkFailed from run {WorkerRunId} for repository {RepositoryId} issue {IssueNumber} blocker {BlockerNumber} was ignored: {Mismatch}.",
                workerRunId.Value,
                @event.MonitoredRepositoryId,
                @event.IssueNumber,
                @event.BlockerNumber,
                DescribeMismatch(match));
            return;
        }

        TriageFailedIssue failed = blocked.FailBlockerLink(
            workerRunId,
            @event.BlockerNumber,
            @event.ProviderMessage,
            @event.FailedAt);
        await db.TransitionAsync(blocked, failed, domainEventDispatcher, cancellationToken);
    }

    private static string DescribeMismatch(LinkRefusalMatch match) =>
        match switch
        {
            LinkRefusalMatch.FromAnotherRun => "the refusal is from another run than the one behind the issue's verdict",
            LinkRefusalMatch.BlockerNotNamed => "the blocker is not named by the verdict",
            _ => throw new UnreachableException($"A {match} refusal is not a mismatch."),
        };
}
