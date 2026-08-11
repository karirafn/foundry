namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class ProviderTypes
{
    internal const string GitHub = "github";
    internal const string GitLab = "gitlab";

    internal static bool IsKnown(string? providerType) =>
        string.Equals(providerType, GitHub, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(providerType, GitLab, StringComparison.OrdinalIgnoreCase);
}
