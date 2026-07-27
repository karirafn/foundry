using System.Collections.Frozen;

namespace Foundry.Modules.Monitoring.Features.Accounts;

// Values here are UI display strings only — permission labels for GitHub fine-grained tokens
// and OAuth scope identifiers for GitLab. These are shown in the account form to guide token creation.
// They are NOT a validation source for GitHub: scope validation uses the inlined constant in GitHubHttpClient,
// which checks X-OAuth-Scopes headers for classic PATs (the only token type that carries those headers).
internal static class RequiredScopes
{
    private static readonly FrozenDictionary<string, IReadOnlyList<string>> Scopes =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [ProviderTypes.GitHub] =
            [
                "Contents (read and write)",
                "Issues (read and write)",
                "Pull requests (read and write)",
                "Workflows (write)",
                "Metadata (read)",
            ],
            [ProviderTypes.GitLab] = ["api"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> For(string providerType) =>
        Scopes.TryGetValue(providerType, out IReadOnlyList<string>? scopes) ? scopes : [];
}
