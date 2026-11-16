using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>Reads stored Package Source verdicts; never probes.</summary>
internal interface IPackageSourceVerdicts
{
    /// <summary>The stored verdict for <paramref name="sourceUrl"/> and <paramref name="credential"/>; Unchecked when none.</summary>
    Task<PackageSourceVerdict> VerdictForAsync(
        Uri sourceUrl,
        ProbeCredential credential,
        CancellationToken cancellationToken);
}

internal sealed class PackageSourceVerdicts(DbContext dbContext) : IPackageSourceVerdicts
{
    public async Task<PackageSourceVerdict> VerdictForAsync(
        Uri sourceUrl,
        ProbeCredential credential,
        CancellationToken cancellationToken)
    {
        PackageSource? source = await dbContext.Set<PackageSource>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ServiceIndexUrl == sourceUrl, cancellationToken);

        return source is null ? new PackageSourceVerdict.Unchecked() : source.VerdictFor(credential);
    }
}
