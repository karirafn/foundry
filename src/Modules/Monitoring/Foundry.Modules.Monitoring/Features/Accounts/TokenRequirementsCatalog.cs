using System.Collections.Frozen;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class TokenRequirementsCatalog
{
    private static readonly FrozenDictionary<string, TokenRequirements> Catalog =
        new Dictionary<string, TokenRequirements>
        {
            [ProviderTypes.GitHub] = new(
                ProviderType: ProviderTypes.GitHub,
                TokenTypeLabel: "GitHub fine-grained personal access token",
                Scopes: RequiredScopes.For(ProviderTypes.GitHub),
                CreationUrlTemplate: "{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write&issues=write&pull_requests=write&workflows=write"),
            [ProviderTypes.GitLab] = new(
                ProviderType: ProviderTypes.GitLab,
                TokenTypeLabel: "GitLab personal access token",
                Scopes: RequiredScopes.For(ProviderTypes.GitLab),
                CreationUrlTemplate: "{baseUrl}/-/user_settings/personal_access_tokens"),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static TokenRequirements? For(string providerType) =>
        Catalog.TryGetValue(providerType, out TokenRequirements? requirements) ? requirements : null;
}
