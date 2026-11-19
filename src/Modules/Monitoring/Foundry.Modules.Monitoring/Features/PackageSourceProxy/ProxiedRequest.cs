namespace Foundry.Modules.Monitoring.Features.PackageSourceProxy;

/// <summary>The upstream request a proxied request maps to.</summary>
internal abstract record ProxiedRequest(Uri Url)
{
    /// <summary>A URL under the service index's directory: forwarded as it is.</summary>
    internal sealed record UnderDirectory(Uri Url) : ProxiedRequest(Url);

    /// <summary>
    /// A GitLab group feed's project download: forwarded only once the feed's registration index for
    /// <paramref name="PackageId"/> lists <paramref name="Url"/>.
    /// </summary>
    internal sealed record GroupFeedDownload(Uri Url, string PackageId) : ProxiedRequest(Url);
}
