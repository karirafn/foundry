using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Runs;

internal sealed class RunningBranchQueries(DbContext db) : IRunningBranchQueries
{
    public async Task<bool> IsRunningBranchAsync(
        Guid monitoredRepositoryId,
        string branchName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return false;
        }

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(monitoredRepositoryId);
        BranchName branch = BranchName.From(branchName);

        return await db.Set<ActiveRun>()
            .AsNoTracking()
            .AnyAsync(r => r.MonitoredRepositoryId == repositoryId && r.BranchName == branch, cancellationToken);
    }
}
