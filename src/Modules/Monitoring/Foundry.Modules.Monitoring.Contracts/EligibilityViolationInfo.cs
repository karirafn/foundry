namespace Foundry.Modules.Monitoring.Contracts;

public sealed record EligibilityViolationInfo(string Rule, string Description)
{
    public static readonly string AllowDirectPushesRule = "branch-protection:allow-direct-pushes";
    public static readonly string AllowForcePushesRule = "branch-protection:allow-force-pushes";
    public static readonly string AllowDeletionRule = "branch-protection:allow-deletion";
    public static readonly string UnreachableRule = "branch-protection:unreachable";
    public static readonly string AllowDirectPushesDescription = "Allows direct pushes to the protected branch.";
    public static readonly string AllowForcePushesDescription = "Allows force pushes to the protected branch.";
    public static readonly string AllowDeletionDescription = "Allows deletion of the protected branch.";
    public static readonly string UnreachableDescription = "Branch protection could not be verified.";
}
