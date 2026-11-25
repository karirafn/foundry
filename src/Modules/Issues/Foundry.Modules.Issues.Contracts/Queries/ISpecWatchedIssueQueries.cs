using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

/// <summary>The issues with open work whose spec Monitoring watches for a rewrite (ADR 0125).</summary>
public interface ISpecWatchedIssueQueries
{
    /// <summary>
    /// The repository's issues waiting to start a run or waiting on a person while they have open work:
    /// <c>revision_queued</c>, <c>revision_blocked</c>, <c>continuation_queued</c>, <c>continuation_blocked</c>,
    /// <c>review</c> and <c>revision_failed</c>.
    /// </summary>
    Task<IReadOnlyList<SpecWatchedIssue>> GetSpecWatchedIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken);
}

/// <summary>
/// An issue whose spec is watched, with the fingerprint its last triage judged, or <see langword="null"/> when no triage
/// recorded one.
/// </summary>
public sealed record SpecWatchedIssue(int IssueNumber, SpecHash? TriagedSpecHash);
