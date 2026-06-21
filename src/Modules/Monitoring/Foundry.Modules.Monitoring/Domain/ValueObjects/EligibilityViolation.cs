using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

public sealed record EligibilityViolation
{
    public static readonly string AllowDirectPushesRule = EligibilityViolationInfo.AllowDirectPushesRule;
    public static readonly string AllowForcePushesRule = EligibilityViolationInfo.AllowForcePushesRule;
    public static readonly string AllowDeletionRule = EligibilityViolationInfo.AllowDeletionRule;
    public static readonly string UnreachableRule = EligibilityViolationInfo.UnreachableRule;

    public string Rule { get; }
    public string Description { get; }

    // Internal to allow JSON deserialization by the EF value converter.
    // External consumers must use the factory methods (AllowDirectPushes, AllowForcePushes, etc.)
    // to ensure the Rule and Description are always consistent.
    [JsonConstructor]
    internal EligibilityViolation(string rule, string description)
    {
        Rule = rule;
        Description = description;
    }

    public static EligibilityViolation AllowDirectPushes() =>
        new(AllowDirectPushesRule, EligibilityViolationInfo.AllowDirectPushesDescription);

    public static EligibilityViolation AllowForcePushes() =>
        new(AllowForcePushesRule, EligibilityViolationInfo.AllowForcePushesDescription);

    public static EligibilityViolation AllowDeletion() =>
        new(AllowDeletionRule, EligibilityViolationInfo.AllowDeletionDescription);

    public static EligibilityViolation Unreachable() =>
        new(UnreachableRule, EligibilityViolationInfo.UnreachableDescription);
}
