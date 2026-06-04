namespace Foundry.Modules.Monitoring.Features;

public sealed record BranchProtection(
    string DefaultBranch,
    bool RejectDirectPushes,
    bool RejectForcePushes,
    bool RejectDeletion);
