using Foundry.Modules.Issues.Domain.ValueObjects;

namespace Foundry.Modules.Issues.Features;

/// <summary>
/// Builds web URLs in the issue's repository from the issue's provider URL: GitHub issues live at
/// <c>/owner/repo/issues/N</c> and the repository's pages under <c>/owner/repo/</c>; GitLab issues live at
/// <c>/group/project/-/issues/N</c> and the repository's pages under <c>/group/project/-/</c>. Each builder returns
/// <see langword="null"/> when the issue URL has neither provider's shape.
/// </summary>
internal static class ProviderWebUrl
{
    private const string GitLabSeparator = "/-/";
    private const string GitHubIssuesSegment = "/issues/";

    /// <summary>The branch's web URL.</summary>
    public static string? Branch(ProviderUrl issueUrl, string branchName)
    {
        string escapedBranch = string.Join('/', branchName.Split('/').Select(Uri.EscapeDataString));
        return RepositoryPage(issueUrl, $"tree/{escapedBranch}");
    }

    /// <summary>The provider's compare view of the commits from <paramref name="fromSha"/> to <paramref name="toSha"/>.</summary>
    public static string? Compare(ProviderUrl issueUrl, string fromSha, string toSha) =>
        RepositoryPage(issueUrl, $"compare/{Uri.EscapeDataString(fromSha)}...{Uri.EscapeDataString(toSha)}");

    /// <summary>The commit's web URL.</summary>
    public static string? Commit(ProviderUrl issueUrl, string sha) =>
        RepositoryPage(issueUrl, $"commit/{Uri.EscapeDataString(sha)}");

    private static string? RepositoryPage(ProviderUrl issueUrl, string page)
    {
        Uri uri = issueUrl.Value;
        string path = uri.AbsolutePath;
        string origin = uri.GetLeftPart(UriPartial.Authority);

        int gitLabIndex = path.IndexOf(GitLabSeparator, StringComparison.Ordinal);
        if (gitLabIndex > 0)
        {
            return $"{origin}{path[..gitLabIndex]}/-/{page}";
        }

        int gitHubIndex = path.LastIndexOf(GitHubIssuesSegment, StringComparison.Ordinal);
        if (gitHubIndex > 0)
        {
            return $"{origin}{path[..gitHubIndex]}/{page}";
        }

        return null;
    }
}
