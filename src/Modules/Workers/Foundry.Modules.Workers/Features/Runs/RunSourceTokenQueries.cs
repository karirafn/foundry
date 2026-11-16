using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities.States;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Runs;

internal sealed class RunSourceTokenQueries(DbContext db) : IRunSourceTokenQueries
{
    public async Task<Guid?> FindLiveRunRepositoryAsync(RunSourceTokenHash tokenHash, CancellationToken cancellationToken)
    {
        RunSourceTokenHash? hash = tokenHash;

        // An active run names its repository; a starting run names the one the Publisher prepared its base from.
        MonitoredRepositoryId? activeRepository = await db.Set<ActiveRun>()
            .AsNoTracking()
            .Where(r => r.SourceTokenHash == hash)
            .Select(r => (MonitoredRepositoryId?)r.MonitoredRepositoryId)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeRepository is { } active)
        {
            return active.Value;
        }

        MonitoredRepositoryId? startingRepository = await db.Set<StartingRun>()
            .AsNoTracking()
            .Where(r => r.SourceTokenHash == hash)
            .Select(r => r.PreparedRepositoryId)
            .FirstOrDefaultAsync(cancellationToken);

        return startingRepository?.Value;
    }
}
