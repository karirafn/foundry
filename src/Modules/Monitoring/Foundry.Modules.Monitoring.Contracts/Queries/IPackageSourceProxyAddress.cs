namespace Foundry.Modules.Monitoring.Contracts.Queries;

/// <summary>Where a worker container reaches the Package Source Proxy (ADR 0112).</summary>
public interface IPackageSourceProxyAddress
{
    /// <summary>The proxy's base URL as a worker sees it, ending in <c>/package-sources/</c>.</summary>
    Uri WorkerBaseUrl { get; }
}
