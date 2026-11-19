namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>Runs the Publisher's proxy-probe mode: one request to the Package Source Proxy from a throwaway container.</summary>
internal interface IPackageProxyProbe
{
    /// <summary>
    /// Requests <paramref name="proxyUrl"/> once from a container whose <c>/etc/hosts</c> carries
    /// <paramref name="extraHostEntry"/>, the entry a worker gets, so the probe takes a worker's network path. Never throws
    /// for a Docker failure; it returns <see cref="PackageProxyProbeOutcome.Inconclusive"/>.
    /// </summary>
    Task<PackageProxyProbeOutcome> ProbeAsync(Uri proxyUrl, string extraHostEntry, CancellationToken cancellationToken);
}
