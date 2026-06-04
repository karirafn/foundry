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
        new(AllowDirectPushesRule, EligibilityViolationInfo.AllowDirectPushesDescription);

    public static EligibilityViolation AllowForcePushes() =>
        new(AllowForcePushesRule, EligibilityViolationInfo.AllowForcePushesDescription);

    public static EligibilityViolation AllowDeletion() =>
        new(AllowDeletionRule, EligibilityViolationInfo.AllowDeletionDescription);

    public static EligibilityViolation Unreachable() =>
        new(UnreachableRule, EligibilityViolationInfo.UnreachableDescription);
}
