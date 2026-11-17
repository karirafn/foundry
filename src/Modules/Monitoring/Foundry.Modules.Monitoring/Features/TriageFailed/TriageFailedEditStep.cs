using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.TriageFailed;

/// <summary>
/// The failed-triage step (issue #773, ADR 0097): an edit to a <c>triage_failed</c> issue's description sends it back
/// to triage. On every scheduled poll it reads each such issue's body and keeps the issue's <see cref="IssueWatch"/>:
/// the first successful read is the baseline, a different body becomes a pending change, and once that change has
/// outlasted the quiet period it raises <see cref="TriageFailedIssueEdited"/> and removes the watch in the same save.
/// Watches of issues no longer <c>triage_failed</c> are removed.
/// </summary>
internal sealed class TriageFailedEditStep(
    ITriageFailedIssueQueries triageFailedQueries,
    DbContext dbContext,
    IIntegrationEventDispatcher integrationEventDispatcher,
    ILogger<TriageFailedEditStep> logger)
{
    public async Task RunAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<int> failed = await triageFailedQueries.GetTriageFailedIssueNumbersAsync(repository.Id, cancellationToken);

        Dictionary<int, IssueWatch> watches = await dbContext.Set<IssueWatch>()
            .Where(watch => watch.MonitoredRepositoryId == repository.Id)
            .ToDictionaryAsync(watch => watch.IssueNumber, cancellationToken);

        ForgetWatchesOfIssuesNoLongerFailed(watches, failed);

        List<IIntegrationEvent> events = [];

        // HTTP calls are sequential: failed triages per repository are few and operator-visible (issue #773).
        foreach (int issueNumber in failed.Order())
        {
            // A failed read changes nothing; the next poll reads again. A closed issue is left to the listing's close path.
            if (await provider.GetIssueStatusAsync(repository.Slug, issueNumber, cancellationToken)
                is not Result<IssueStatus>.Success { Value: { IsClosed: false } status })
            {
                continue;
            }

            IssueBodyHash body = IssueBodyHash.Of(status.Body);

            if (!watches.TryGetValue(issueNumber, out IssueWatch? watch) || watch.Wait is not WatchedWait.TriageFailed)
            {
                ReplaceWatch(watches, IssueWatch.StartForFailedTriage(repository.Id, issueNumber, body));
                continue;
            }

            watch.ObserveBody(body, now);

            if (watch.SettleBodyChange(now) is InputSettlement.Settled settled)
            {
                events.Add(new TriageFailedIssueEdited(repository.Id, issueNumber, settled.ArrivedAt));
                dbContext.Set<IssueWatch>().Remove(watch);
                watches.Remove(issueNumber);
                logger.LogInformation(
                    "The description of triage-failed issue #{IssueNumber} in '{Slug}' changed; sending it back to triage.",
                    issueNumber,
                    repository.Slug);
            }
        }

        await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Removes the failed-triage watches of issues that left <c>triage_failed</c> by any other exit (Retry triage, Skip
    /// triage, an automatic retry, closing). An awaiting-input watch belongs to the awaiting-input step.
    /// </summary>
    private void ForgetWatchesOfIssuesNoLongerFailed(Dictionary<int, IssueWatch> watches, IReadOnlySet<int> failed)
    {
        foreach (IssueWatch stale in watches.Values
            .Where(watch => watch.Wait is WatchedWait.TriageFailed && !failed.Contains(watch.IssueNumber))
            .ToList())
        {
            dbContext.Set<IssueWatch>().Remove(stale);
            watches.Remove(stale.IssueNumber);
        }
    }

    /// <summary>
    /// Starts the issue's watch, replacing one left from an awaiting-input wait the awaiting-input step has not removed
    /// yet: its baseline predates the failure.
    /// </summary>
    private void ReplaceWatch(Dictionary<int, IssueWatch> watches, IssueWatch watch)
    {
        if (watches.Remove(watch.IssueNumber, out IssueWatch? previous))
        {
            dbContext.Set<IssueWatch>().Remove(previous);
        }

        dbContext.Set<IssueWatch>().Add(watch);
        watches[watch.IssueNumber] = watch;
    }
}
