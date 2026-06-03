namespace Foundry.Modules.Monitoring.Contracts;

public sealed record EligibilityViolationInfo(string Rule, string Description)
{
    public static readonly string AllowDirectPushesRule = "branch-protection:allow-direct-pushes";
    public static readonly string AllowForcePushesRule = "branch-protection:allow-force-pushes";
    public static readonly string AllowDeletionRule = "branch-protection:allow-deletion";
    public static readonly string UnreachableRule = "branch-protection:unreachable";
}
