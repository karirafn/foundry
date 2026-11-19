namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal static class GitHubTokenKind
{
    private const string FineGrainedPatPrefix = "github_pat_";

    internal static bool IsFineGrainedPat(string token) =>
        token.StartsWith(FineGrainedPatPrefix, StringComparison.Ordinal);
}
