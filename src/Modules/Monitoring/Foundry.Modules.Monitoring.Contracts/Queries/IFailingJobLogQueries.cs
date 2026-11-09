using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>Reads the CI job log behind a failing check (one provider call per job, ADR 0066).</summary>
public interface IFailingJobLogQueries
{
    /// <summary>
    /// Resolves <paramref name="check"/> to its CI job and reads that job's log. A failure (expired, not found,
    /// transient) is never a reason to block a revision: callers list the check without a log.
    /// </summary>
    Task<Result<CiJobLog>> GetFailingJobLogAsync(
        MonitoredRepositoryId repositoryId,
        FailingCheck check,
        CancellationToken cancellationToken);
}
