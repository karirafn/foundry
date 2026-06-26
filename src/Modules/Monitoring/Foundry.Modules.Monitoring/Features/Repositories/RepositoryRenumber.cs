using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// Two-phase renumber helper for <see cref="MonitoredRepository"/> positions.
/// SQLite does not support deferrable unique constraints, so a single-phase
/// renumber can produce transient collisions mid-flush. Two phases avoid this:
///   Phase 1 — shift all affected rows into a collision-free offset band,
///   Phase 2 — assign final contiguous 0..n-1 positions.
/// The caller is responsible for opening and committing the transaction.
/// </summary>
internal static class RepositoryRenumber
{
    private const int OffsetBand = 1_000_000;

    internal static async Task RenumberAsync(
        DbContext dbContext,
        IReadOnlyList<MonitoredRepository> orderedRepositories,
        CancellationToken cancellationToken)
    {
        // Phase 1 — offset every position into a collision-free band.
        for (int i = 0; i < orderedRepositories.Count; i++)
        {
            orderedRepositories[i].SetPosition(OffsetBand + i);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Phase 2 — assign final contiguous 0..n-1 positions.
        for (int i = 0; i < orderedRepositories.Count; i++)
        {
            orderedRepositories[i].SetPosition(i);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
