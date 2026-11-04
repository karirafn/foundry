using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// Provider-neutral errors for <see cref="IIssueProvider.ApplyBranchRulesAsync"/>. Every other
/// provider failure keeps its provider-specific error code.
/// </summary>
internal static class BranchRuleWriteErrors
{
    public const string InsufficientPermissionCode = "BranchRules.InsufficientPermission";

    public static Error InsufficientPermission(string message) =>
        new(InsufficientPermissionCode, message);
}
