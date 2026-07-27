namespace Foundry.Modules.Monitoring.Features.Providers;

public sealed record BranchProtection(
    string DefaultBranch,
    bool RejectDirectPushes,
    bool RejectForcePushes,
    bool RejectDeletion);
