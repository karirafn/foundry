using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.SpecRewrite;

/// <summary>
/// The spec-rewrite step (ADR 0125): a spec rewritten after an issue has open work sends it back to triage. On every
/// scheduled poll it reads the body of each issue waiting to start a run or waiting on a person with open work, and
/// keeps the issue's <see cref="IssueWatch"/> on its <see cref="IssueBodyHash.OfSpec"/> hash: the baseline is the hash
/// the issue's last triage judged (the first read when none was recorded), a different spec becomes a pending change,
/// and once that change has outlasted the quiet period it raises <see cref="IssueSpecRewritten"/> and removes the
/// watch in the same save. Watches of issues no longer in a watched state are removed.
/// </summary>
internal sealed class SpecRewriteStep(
    ISpecWatchedIssueQueries specWatchedQueries,
    DbContext dbContext,
    IIntegrationEventDispatcher integrationEventDispatcher,
    ILogger<SpecRewriteStep> logger)
{
    public async Task RunAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SpecWatchedIssue> watched = await specWatchedQueries.GetSpecWatchedIssuesAsync(
            repository.Id,
            cancellationToken);

        Dictionary<int, IssueWatch> watches = await dbContext.Set<IssueWatch>()
            .Where(watch => watch.MonitoredRepositoryId == repository.Id)
            .ToDictionaryAsync(watch => watch.IssueNumber, cancellationToken);

        ForgetWatchesOfIssuesNoLongerWatched(watches, [.. watched.Select(issue => issue.IssueNumber)]);

        List<IIntegrationEvent> events = [];

        // HTTP calls are sequential, one body read per watched issue: issues with open work per repository are few.
        foreach (SpecWatchedIssue issue in watched)
        {
            // A failed read changes nothing; the next poll reads again. A closed issue is left to the listing's close path.
            if (await provider.GetIssueStatusAsync(repository.Slug, issue.IssueNumber, cancellationToken)
                is not Result<IssueStatus>.Success { Value: { IsClosed: false } status })
            {
                continue;
            }

            IssueBodyHash spec = IssueBodyHash.OfSpec(status.Body);
            IssueWatch watch = WatchOf(repository, issue, spec, watches);

            watch.ObserveBody(spec, now);

            if (watch.SettleBodyChange(now) is InputSettlement.Settled settled)
            {
                events.Add(new IssueSpecRewritten(repository.Id, issue.IssueNumber, settled.ArrivedAt));
                dbContext.Set<IssueWatch>().Remove(watch);
                watches.Remove(issue.IssueNumber);
                logger.LogInformation(
                    "The spec of issue #{IssueNumber} in '{Slug}' was rewritten since its last triage; sending it back to triage.",
                    issue.IssueNumber,
                    repository.Slug);
            }
        }

        await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Removes the spec watches of issues that left the watched states (a run started, the code-owned pipeline, a
    /// question, closing). Another wait's watch belongs to its own step.
    /// </summary>
    private void ForgetWatchesOfIssuesNoLongerWatched(Dictionary<int, IssueWatch> watches, HashSet<int> watched)
    {
        foreach (IssueWatch stale in watches.Values
            .Where(watch => watch.Wait is WatchedWait.SpecChanged && !watched.Contains(watch.IssueNumber))
            .ToList())
        {
            dbContext.Set<IssueWatch>().Remove(stale);
            watches.Remove(stale.IssueNumber);
        }
    }

    /// <summary>
    /// The issue's spec watch. One is started when the issue has none, when another wait's watch is left over, or when
    /// a triage since the watch started recorded a different spec: the comparison is always against the spec triage
    /// last judged, so a rewrite made during a run or the pipeline is still a change here.
    /// </summary>
    private IssueWatch WatchOf(
        MonitoredRepository repository,
        SpecWatchedIssue issue,
        IssueBodyHash currentSpec,
        Dictionary<int, IssueWatch> watches)
    {
        IssueBodyHash? triaged = issue.TriagedSpecHash is { } hash ? IssueBodyHash.FromStored(hash.Value) : null;

        if (watches.TryGetValue(issue.IssueNumber, out IssueWatch? existing)
            && existing.Wait is WatchedWait.SpecChanged
            && (triaged is null || existing.BaselineBodyHash == triaged))
        {
            return existing;
        }

        IssueWatch started = IssueWatch.StartForSpecChange(repository.Id, issue.IssueNumber, triaged ?? currentSpec);

        if (watches.Remove(issue.IssueNumber, out IssueWatch? previous))
        {
            dbContext.Set<IssueWatch>().Remove(previous);
        }

        dbContext.Set<IssueWatch>().Add(started);
        watches[issue.IssueNumber] = started;
        return started;
    }
}
