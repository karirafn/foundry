using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.Providers;

internal sealed record BranchProtection(
    string DefaultBranch,
    bool RejectDirectPushes,
    bool RejectForcePushes,
    bool RejectDeletion)
{
    /// <summary>
    /// The default-branch rules eligibility requires that this protection does not enforce.
    /// Empty when the branch satisfies every rule. This is the single definition of the
    /// branch-protection violation set; eligibility composition and branch-rule enforcement
    /// both read it from here.
    /// </summary>
    public IReadOnlyList<EligibilityViolation> Violations
    {
        get
        {
            List<EligibilityViolation> violations = [];

            if (!RejectDirectPushes)
            {
                violations.Add(EligibilityViolation.AllowDirectPushes());
            }

            if (!RejectForcePushes)
            {
                violations.Add(EligibilityViolation.AllowForcePushes());
            }

            if (!RejectDeletion)
            {
                violations.Add(EligibilityViolation.AllowDeletion());
            }

            return violations;
        }
    }
}
