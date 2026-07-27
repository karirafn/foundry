using System.Collections.Frozen;

namespace Foundry.Modules.Monitoring.Features.Accounts;

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
