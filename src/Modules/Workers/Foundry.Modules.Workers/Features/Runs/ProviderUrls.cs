using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Runs;

/// <summary>Provider web URLs the API derives from a run's pull request URL, so the dashboard never composes one.</summary>
internal static class ProviderUrls
{
    private const string GitHubPullSegment = "/pull/";
    private const string GitLabMergeRequestSegment = "/-/merge_requests/";

    /// <summary>
    /// The web URL of <paramref name="sha"/> in the repository of <paramref name="pullRequestUrl"/>, or
    /// <see langword="null"/> when there is no pull request to derive the repository from.
    /// </summary>
    public static string? CommitUrl(string? pullRequestUrl, CommitSha sha)
    {
        if (pullRequestUrl is null)
        {
            return null;
        }

        int gitLab = pullRequestUrl.IndexOf(GitLabMergeRequestSegment, StringComparison.Ordinal);
        if (gitLab > 0)
        {
            return $"{pullRequestUrl[..gitLab]}/-/commit/{sha.Value}";
        }

        int gitHub = pullRequestUrl.IndexOf(GitHubPullSegment, StringComparison.Ordinal);
        return gitHub > 0 ? $"{pullRequestUrl[..gitHub]}/commit/{sha.Value}" : null;
    }
}
