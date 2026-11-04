using System.Diagnostics;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

/// <summary>Builds the provider's branch-rules settings page for a repository.</summary>
internal static class BranchRulesSettingsUrl
{
    public static string For(Credential account, RepositorySlug slug)
    {
        string baseUrl = account.BaseUrl.Value.ToString().TrimEnd('/');

        return account switch
        {
            GitHubCredential => $"{baseUrl}/{slug.FullPath}/settings/rules",
            GitLabCredential => $"{baseUrl}/{slug.FullPath}/-/settings/repository#js-protected-branches-settings",
            _ => throw new UnreachableException($"Unhandled credential type: {account.GetType().Name}"),
        };
    }
}
