using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Domain;

public sealed record EligibilityViolation
{
    public static string AllowDirectPushesRule => EligibilityViolationInfo.AllowDirectPushesRule;
    public static string AllowForcePushesRule => EligibilityViolationInfo.AllowForcePushesRule;
    public static string AllowDeletionRule => EligibilityViolationInfo.AllowDeletionRule;
    public static string UnreachableRule => EligibilityViolationInfo.UnreachableRule;

    public string Rule { get; }
    public string Description { get; }

    private EligibilityViolation(string rule, string description)
    {
        Rule = rule;
        Description = description;
    }

    internal static EligibilityViolation From(string rule, string description) =>
        new(rule, description);

    public static EligibilityViolation AllowDirectPushes() =>
        new(
            AllowDirectPushesRule,
            "The repository allows direct pushes to the protected branch, which could allow bypassing the worker's pull request workflow.");

    public static EligibilityViolation AllowForcePushes() =>
        new(
            AllowForcePushesRule,
            "The repository allows force pushes to the protected branch, which could allow overwriting the worker's commits.");

    public static EligibilityViolation AllowDeletion() =>
        new(
            AllowDeletionRule,
            "The repository allows deletion of the protected branch, which could result in loss of the worker's work.");

    public static EligibilityViolation Unreachable(string message) =>
        new(
            UnreachableRule,
            message);
}
