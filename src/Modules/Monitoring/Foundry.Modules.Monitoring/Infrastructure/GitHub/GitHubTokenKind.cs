namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal static class GitHubTokenKind
{
    private const string FineGrainedPatPrefix = "github_pat_";

    internal static bool IsFineGrainedPat(string token) =>
        token.StartsWith(FineGrainedPatPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Why Foundry refuses <paramref name="token"/> before checking its permissions, or <see langword="null"/>
    /// for a fine-grained personal access token. Every other prefix, including ones GitHub adds later, is
    /// refused (ADR 0115).
    /// </summary>
    internal static WrongTokenKind? WrongKindOf(string token) =>
        IsFineGrainedPat(token) ? null : WrongTokenKind.GitHubClassic;
}
