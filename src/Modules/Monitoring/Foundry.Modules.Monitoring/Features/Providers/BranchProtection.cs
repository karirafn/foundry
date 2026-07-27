namespace Foundry.Modules.Monitoring.Features.Providers;

internal sealed record BranchProtection(
    string DefaultBranch,
    bool RejectDirectPushes,
    bool RejectForcePushes,
    bool RejectDeletion);
