using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

internal interface IGitHubWriteProber
{
    Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken);
}
