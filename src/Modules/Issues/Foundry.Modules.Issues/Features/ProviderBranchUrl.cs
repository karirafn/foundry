using Foundry.Modules.Issues.Domain.ValueObjects;

namespace Foundry.Modules.Issues.Features;

/// <summary>
/// Builds a branch's web URL from the issue's provider URL: GitHub issues live at <c>/owner/repo/issues/N</c> and
/// branches at <c>/owner/repo/tree/branch</c>; GitLab issues live at <c>/group/project/-/issues/N</c> and branches at
/// <c>/group/project/-/tree/branch</c>.
/// </summary>
internal static class ProviderBranchUrl
{
    private const string GitLabSeparator = "/-/";
    private const string GitHubIssuesSegment = "/issues/";

    /// <summary>The branch's web URL, or <see langword="null"/> when the issue URL has neither provider's shape.</summary>
    public static string? For(ProviderUrl issueUrl, string branchName)
    {
        Uri uri = issueUrl.Value;
        string path = uri.AbsolutePath;
        string escapedBranch = string.Join('/', branchName.Split('/').Select(Uri.EscapeDataString));
        string origin = uri.GetLeftPart(UriPartial.Authority);

        int gitLabIndex = path.IndexOf(GitLabSeparator, StringComparison.Ordinal);
        if (gitLabIndex > 0)
        {
            return $"{origin}{path[..gitLabIndex]}/-/tree/{escapedBranch}";
        }

        int gitHubIndex = path.LastIndexOf(GitHubIssuesSegment, StringComparison.Ordinal);
        if (gitHubIndex > 0)
        {
            return $"{origin}{path[..gitHubIndex]}/tree/{escapedBranch}";
        }

        return null;
    }
}
