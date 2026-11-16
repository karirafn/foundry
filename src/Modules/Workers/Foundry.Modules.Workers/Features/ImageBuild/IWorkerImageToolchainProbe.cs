using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>Reads the toolchain a worker image carries by running its <c>/opt/foundry/toolchain.sh</c> (ADR 0109).</summary>
internal interface IWorkerImageToolchainProbe
{
    /// <summary>
    /// The image's toolchain, or <see langword="null"/> when the probe could not run, timed out, failed, or printed no
    /// single valid <c>FOUNDRY_TOOLCHAIN</c> line.
    /// </summary>
    Task<Toolchain?> ProbeAsync(string imageId, CancellationToken cancellationToken);
}
