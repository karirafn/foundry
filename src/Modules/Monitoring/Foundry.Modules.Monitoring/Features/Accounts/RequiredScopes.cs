using System.Collections.Frozen;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class RequiredScopes
{
    private static readonly FrozenDictionary<string, IReadOnlyList<string>> Scopes =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderTypes.GitHub] = ["repo"],
            [ProviderTypes.GitLab] = ["api"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> For(string providerType) =>
        Scopes.TryGetValue(providerType, out IReadOnlyList<string>? scopes) ? scopes : [];
}
