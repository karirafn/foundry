using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// Provider-neutral errors for <see cref="IIssueProvider.DeleteBranchAsync"/>. Every other
/// provider failure keeps its provider-specific error code.
/// </summary>
internal static class BranchDeletionErrors
{
    public const string DefaultBranchCode = "BranchDeletion.DefaultBranch";

    public static Error DefaultBranch(string branchName) =>
        new(DefaultBranchCode, $"Refusing to delete '{branchName}': it is the repository's default branch.");
}
