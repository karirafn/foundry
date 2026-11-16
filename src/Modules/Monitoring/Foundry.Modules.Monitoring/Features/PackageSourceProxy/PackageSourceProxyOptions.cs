namespace Foundry.Modules.Monitoring.Features.PackageSourceProxy;

/// <summary>
/// Host configuration for the Package Source Proxy (ADR 0112), bound from the <c>PackageSources</c> section. The proxy
/// is always on: the WebApi binds <see cref="ProxyPort"/> on all interfaces, because worker containers reach the host
/// through <c>host.docker.internal</c>, and serves only <see cref="PathPrefix"/> there.
/// </summary>
public sealed class PackageSourceProxyOptions
{
    public const string SectionName = "PackageSources";

    /// <summary>The configuration key of <see cref="ProxyPort"/>, read before the host is built to bind Kestrel.</summary>
    public const string ProxyPortKey = $"{SectionName}:{nameof(ProxyPort)}";

    /// <summary>The proxy port when <see cref="ProxyPortKey"/> is unset.</summary>
    public const int DefaultProxyPort = 5287;

    /// <summary>The only paths the proxy port serves, and that no other port serves.</summary>
    public const string PathPrefix = "/package-sources";

    /// <summary>The host name a worker container reaches the Foundry host by.</summary>
    public const string WorkerHostName = "host.docker.internal";

    public int ProxyPort { get; set; } = DefaultProxyPort;

    /// <summary>The proxy's base URL as a worker sees it.</summary>
    public Uri WorkerBaseUrl => new($"http://{WorkerHostName}:{ProxyPort}{PathPrefix}/");
}
