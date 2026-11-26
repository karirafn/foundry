namespace Foundry.Modules.Monitoring.Infrastructure;

/// <summary>A kind of account token Foundry refuses, whatever permissions it holds.</summary>
internal enum WrongTokenKind
{
    /// <summary>A GitHub token that is not a fine-grained personal access token.</summary>
    GitHubClassic,

    /// <summary>A GitLab legacy personal access token, which reports scopes instead of a grant.</summary>
    GitLabLegacy,

    /// <summary>A GitLab group or project access token, which cannot read its own grant.</summary>
    GitLabResourceToken,

    /// <summary>A GitLab older than 19.5, which does not report a fine-grained token's grant.</summary>
    GitLabVersion,
}

internal static class WrongTokenKinds
{
    /// <summary>The name the API sends for <paramref name="kind"/>.</summary>
    internal static string WireName(this WrongTokenKind kind) => kind switch
    {
        WrongTokenKind.GitHubClassic => "github-classic",
        WrongTokenKind.GitLabLegacy => "gitlab-legacy",
        WrongTokenKind.GitLabResourceToken => "gitlab-resource-token",
        WrongTokenKind.GitLabVersion => "gitlab-version",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown token kind."),
    };
}
