using System.Diagnostics;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

/// <summary>
/// Builds the provider REST URL base for issues in a repository, reusing the already-derived
/// <see cref="Credential.ApiBaseUrl"/> so that provider-specific path shapes
/// (GitHub repos/, GitLab projects/ with percent-encoded full path) are consistent
/// with the URL patterns the HTTP clients already use.
/// </summary>
internal static class IssueApiUrlBuilder
{
    /// <summary>
    /// Returns the issue-list URL base without a trailing issue number, e.g.
    /// <c>https://api.github.com/repos/owner/name/issues</c>. Append
    /// <c>/{issueNumber}</c> to get the single-issue URL.
    /// </summary>
    internal static string BuildBase(Credential credential, RepositorySlug slug)
    {
        string apiBase = credential.ApiBaseUrl.ToString().TrimEnd('/');

        return credential switch
        {
            GitHubCredential => BuildGitHubBase(apiBase, slug),
            GitLabCredential => BuildGitLabBase(apiBase, slug),
            _ => throw new UnreachableException($"Unknown credential type: {credential.GetType().Name}"),
        };
    }

    private static string BuildGitHubBase(string apiBase, RepositorySlug slug)
    {
        string owner = Uri.EscapeDataString(slug.Owner);
        string name = Uri.EscapeDataString(slug.Name);
        return $"{apiBase}/repos/{owner}/{name}/issues";
    }

    private static string BuildGitLabBase(string apiBase, RepositorySlug slug)
    {
        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        return $"{apiBase}/projects/{encodedPath}/issues";
    }
}
