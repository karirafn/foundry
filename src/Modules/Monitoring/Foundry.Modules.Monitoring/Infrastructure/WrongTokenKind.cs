namespace Foundry.Modules.Monitoring.Infrastructure;

/// <summary>A kind of account token Foundry refuses, whatever permissions it holds.</summary>
internal enum WrongTokenKind
{
    /// <summary>A GitHub token that is not a fine-grained personal access token.</summary>
    GitHubClassic,
}

internal static class WrongTokenKinds
{
    /// <summary>The name the API sends for <paramref name="kind"/>.</summary>
    internal static string WireName(this WrongTokenKind kind) => kind switch
    {
        WrongTokenKind.GitHubClassic => "github-classic",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown token kind."),
    };
}
