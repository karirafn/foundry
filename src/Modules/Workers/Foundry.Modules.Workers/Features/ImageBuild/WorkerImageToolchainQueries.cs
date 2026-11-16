using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>Reads a worker image's recorded toolchain (ADR 0109).</summary>
internal static class WorkerImageToolchainQueries
{
    /// <summary>
    /// The image's recorded toolchain, or <see cref="Toolchain.Unrecorded"/> when the image ID is unknown or no list is
    /// stored for it yet, so every toolchain-dependent prompt section is left out.
    /// </summary>
    public static async Task<Toolchain> GetRecordedToolchainAsync(
        this DbContext dbContext,
        string? imageId,
        CancellationToken cancellationToken)
    {
        if (imageId is null)
        {
            return Toolchain.Unrecorded;
        }

        WorkerImageToolchain? row = await dbContext.Set<WorkerImageToolchain>()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.ImageId == imageId, cancellationToken);

        return row?.Toolchain ?? Toolchain.Unrecorded;
    }
}
