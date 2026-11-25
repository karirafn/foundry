using System.Collections.Frozen;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

internal static class TokenRequirementsCatalog
{
    private static readonly FrozenDictionary<string, TokenRequirements> Catalog =
        new Dictionary<string, TokenRequirements>
        {
            [ProviderTypes.GitHub] = new(
                ProviderType: ProviderTypes.GitHub,
                TokenTypeLabel: "GitHub fine-grained personal access token",
                Permissions: TokenPermissions.RequirementsFor(ProviderTypes.GitHub),
                CreationUrlTemplate: $"{{baseUrl}}/settings/personal-access-tokens/new?name=Foundry&{TokenPermissions.GitHubTemplateQuery}",
                ResourceOwnerHint: "Fine-grained tokens are bound to a single resource owner. To reach an organization's repositories, choose that organization as the token's resource owner when creating the token.",
                ClosingNote: "GitHub can't report a token's extra permissions, so Foundry can't check for any. Grant only the permissions listed here.",
                CreationNote: null),
            [ProviderTypes.GitLab] = new(
                ProviderType: ProviderTypes.GitLab,
                TokenTypeLabel: "GitLab personal access token",
                Permissions: TokenPermissions.RequirementsFor(ProviderTypes.GitLab),
                CreationUrlTemplate: "{baseUrl}/-/user_settings/personal_access_tokens",
                ResourceOwnerHint: null,
                ClosingNote: null,
                CreationNote: null),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static TokenRequirements? For(string providerType) =>
        Catalog.TryGetValue(providerType, out TokenRequirements? requirements) ? requirements : null;
}
