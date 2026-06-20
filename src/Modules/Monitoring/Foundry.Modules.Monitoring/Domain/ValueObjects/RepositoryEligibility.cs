using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

[JsonDerivedType(typeof(Eligible), typeDiscriminator: "eligible")]
[JsonDerivedType(typeof(Ineligible), typeDiscriminator: "ineligible")]
[JsonDerivedType(typeof(Unreachable), typeDiscriminator: "unreachable")]
public abstract record RepositoryEligibility
{
    private RepositoryEligibility() { }

    public sealed record Eligible : RepositoryEligibility;

    public sealed record Ineligible : RepositoryEligibility
    {
        public IReadOnlyList<EligibilityViolation> Violations { get; }

        public Ineligible(IReadOnlyList<EligibilityViolation> violations)
        {
            if (violations.Count == 0)
            {
                throw new ArgumentException("Ineligible must have at least one violation.", nameof(violations));
            }

            Violations = violations;
        }
    }

    public sealed record Unreachable : RepositoryEligibility;

    public static RepositoryEligibility FromBranchProtection(Result<BranchProtection> result)
    {
        if (result is not Result<BranchProtection>.Success success)
        {
            return new Unreachable();
        }

        BranchProtection protection = success.Value;
        List<EligibilityViolation> violations = [];

        if (!protection.RejectDirectPushes)
        {
            violations.Add(EligibilityViolation.AllowDirectPushes());
        }

        if (!protection.RejectForcePushes)
        {
            violations.Add(EligibilityViolation.AllowForcePushes());
        }

        if (!protection.RejectDeletion)
        {
            violations.Add(EligibilityViolation.AllowDeletion());
        }

        if (violations.Count > 0)
        {
            return new Ineligible(violations);
        }

        return new Eligible();
    }
}
