using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Services;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

/// <summary>
/// Moves a triaged issue on by its verdict (ADR 0097): <c>ready</c>, after recording its decisions as a decided
/// clarification round (ADR 0123), through the dependency check to the queue or blocked; <c>needs_input</c> to
/// awaiting input with a re-triage resume target, its decisions on the round it opens; <c>blocked</c> to waiting
/// untriaged on the blockers it names, or to failure (<c>blocker_cycle</c>) when a named blocker would close a
/// dependency cycle through the stored edges of other issues. Every verdict first records the spec fingerprint of the
/// body the run was dispatched with as the issue's triaged spec hash. Replay-safe: an issue no longer in triage with
/// this run (a redelivery, or the issue moved on) is left as it is.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.TriageRunCompleted")]
internal sealed class TriageRunCompletedHandler(
    DbContext db,
    IIssueQueries issueQueries,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<TriageRunCompletedHandler> logger) : IIntegrationEventHandler<TriageRunCompleted>
{
    public async Task HandleAsync(TriageRunCompleted @event, CancellationToken cancellationToken)
    {
        IssueId issueId = IssueId.From(@event.IssueId);
        DateTimeOffset now = timeProvider.GetUtcNow();

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is not TriageInProgressIssue triaging || triaging.WorkerRunId != @event.WorkerRunId)
        {
            logger.LogInformation(
                "TriageRunCompleted for issue {IssueId} (run {WorkerRunId}) found it no longer in triage with that run (state: {State}); ignoring.",
                @event.IssueId,
                @event.WorkerRunId,
                issue?.GetType().Name ?? "not found");
            return;
        }

        if (@event.TriagedSpecHash is { } triagedSpecHash)
        {
            triaging.RecordTriagedSpec(triagedSpecHash);
        }

        Issue next = @event.Result switch
        {
            TriageResult.Ready ready => triaging.MarkReady(
                new TriageVerdict.Ready(ToIssueKind(ready.Kind), @event.DecidedAt),
                ready.Decisions,
                now),
            TriageResult.NeedsInput needsInput => triaging.MarkNeedsInput(
                new TriageVerdict.NeedsInput(ToIssueKind(needsInput.Kind), needsInput.Questions, @event.DecidedAt),
                needsInput.Decisions,
                now),
            TriageResult.Blocked blocked => await RouteBlockedAsync(triaging, blocked, @event.DecidedAt, now, cancellationToken),
            _ => throw new UnreachableException($"Unhandled triage result: {@event.Result.GetType().Name}"),
        };

        await db.TransitionAsync(triaging, next, domainEventDispatcher, cancellationToken);
    }

    private async Task<Issue> RouteBlockedAsync(
        TriageInProgressIssue triaging,
        TriageResult.Blocked blocked,
        DateTimeOffset decidedAt,
        DateTimeOffset transitionedAt,
        CancellationToken cancellationToken)
    {
        if (TriageVerdict.Blocked.RejectionOf(blocked.BlockedBy) is string rejection)
        {
            return triaging.MarkFailed(rejection, FailureCategory.VerdictInvalid, decidedAt);
        }

        IReadOnlyList<DependencyEdge> storedEdges = await issueQueries.GetDependencyGraphAsync(
            triaging.MonitoredRepositoryId,
            cancellationToken);

        DependencyCycle? cycle = DependencyCycleDetector.FindFirstCycleClosedBy(
            storedEdges,
            triaging.IssueNumber,
            [.. blocked.BlockedBy.Select((TriageBlocker blocker) => blocker.IssueNumber)]);

        return cycle is null
            ? triaging.MarkBlocked(
                new TriageVerdict.Blocked(ToIssueKind(blocked.Kind), blocked.BlockedBy, decidedAt),
                transitionedAt)
            : triaging.FailOnBlockerCycle(cycle, decidedAt);
    }

    private static IssueKind ToIssueKind(TriageKind kind) => kind switch
    {
        TriageKind.Feature => IssueKind.Feature,
        TriageKind.Bug => IssueKind.Bug,
        TriageKind.Refactor => IssueKind.Refactor,
        TriageKind.Documentation => IssueKind.Documentation,
        _ => throw new UnreachableException($"Unhandled triage kind: {kind}"),
    };
}
