namespace Foundry.Modules.Monitoring.Contracts;

public sealed record EligibilityViolationInfo(string Rule, string Description)
{
    public static readonly string AllowDirectPushesRule = "branch-protection:allow-direct-pushes";
    public static readonly string AllowForcePushesRule = "branch-protection:allow-force-pushes";
    public static readonly string AllowDeletionRule = "branch-protection:allow-deletion";
    public static readonly string UnreachableRule = "branch-protection:unreachable";
    public static readonly string AllowDirectPushesDescription = "The repository allows direct pushes to the protected branch, which could allow bypassing the worker's pull request workflow.";
    public static readonly string AllowForcePushesDescription = "The repository allows force pushes to the protected branch, which could allow overwriting the worker's commits.";
    public static readonly string AllowDeletionDescription = "The repository allows deletion of the protected branch, which could result in loss of the worker's work.";
    public static readonly string UnreachableDescription = "Branch protection could not be verified. The provider API was unreachable or returned an error.";
}
