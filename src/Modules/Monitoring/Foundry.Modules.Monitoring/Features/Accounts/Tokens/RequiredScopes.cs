namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

// Values here are UI display strings only — permission labels for GitHub fine-grained tokens
// and OAuth scope identifiers for GitLab. These are shown in the account form to guide token creation.
// They are NOT a validation source for GitHub: classic PAT scope validation uses the classic scopes
// of the same TokenPermissions entries, which GitHubHttpClient checks against X-OAuth-Scopes headers.
internal static class RequiredScopes
{
    internal static IReadOnlyList<string> For(string providerType) =>
        TokenPermissions.For(providerType)
            .Where(entry => entry.ShownInForm)
            .Select(entry => entry.Label)
            .ToList();
}
