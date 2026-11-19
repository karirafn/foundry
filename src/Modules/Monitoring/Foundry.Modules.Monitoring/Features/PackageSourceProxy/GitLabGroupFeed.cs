using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Features.PackageSources;

namespace Foundry.Modules.Monitoring.Features.PackageSourceProxy;

/// <summary>
/// A GitLab group NuGet feed. Its service index lists no download resource: each registration entry's
/// <c>packageContent</c> is a project-level URL, <c>&lt;API prefix&gt;/projects/{id}/packages/nuget/download/…</c>,
/// outside the feed's own directory.
/// </summary>
internal sealed partial class GitLabGroupFeed
{
    private readonly Uri _serviceIndexUrl;

    private GitLabGroupFeed(Uri serviceIndexUrl, string apiPrefix)
    {
        _serviceIndexUrl = serviceIndexUrl;
        ApiPrefix = apiPrefix;
    }

    /// <summary>The service-index path before <c>/groups/</c>: <c>/api/v4</c> on gitlab.com.</summary>
    public string ApiPrefix { get; }

    /// <summary>The group feed <paramref name="serviceIndexUrl"/> is, or <see langword="null"/> when it is not one.</summary>
    public static GitLabGroupFeed? Recognize(Uri serviceIndexUrl)
    {
        ArgumentNullException.ThrowIfNull(serviceIndexUrl);

        Match match = GroupServiceIndexPath().Match(serviceIndexUrl.AbsolutePath);
        return match.Success ? new GitLabGroupFeed(serviceIndexUrl, match.Groups["prefix"].Value) : null;
    }

    /// <summary>
    /// The package id, as the path spells it, when <paramref name="url"/> is a project download path on the feed's
    /// origin under its API prefix; otherwise <see langword="null"/>.
    /// </summary>
    public string? PackageIdOfDownload(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        string projectsPath = ApiPrefix + "/projects/";
        if (!PackageSourceUpstreamClient.SameOrigin(url, _serviceIndexUrl)
            || !url.AbsolutePath.StartsWith(projectsPath, StringComparison.Ordinal))
        {
            return null;
        }

        Match match = ProjectDownloadPath().Match(url.AbsolutePath[projectsPath.Length..]);
        if (!match.Success || IsDotSegment(match.Groups["version"].Value) || IsDotSegment(match.Groups["file"].Value))
        {
            return null;
        }

        string packageId = match.Groups["name"].Value;
        return IsDotSegment(packageId) ? null : packageId;
    }

    private static bool IsDotSegment(string segment) => Uri.UnescapeDataString(segment) is "." or "..";

    [GeneratedRegex(@"^(?<prefix>(?:/[^/]+)*/api/v4)/groups/[^/]+/-/packages/nuget/index\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupServiceIndexPath();

    // The package id is held to NuGet's id characters, so it can name a registration path segment as it stands.
    [GeneratedRegex(@"^[0-9]+/packages/nuget/download/(?<name>[A-Za-z0-9._-]+)/(?<version>[^/]+)/(?<file>[^/]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectDownloadPath();
}
