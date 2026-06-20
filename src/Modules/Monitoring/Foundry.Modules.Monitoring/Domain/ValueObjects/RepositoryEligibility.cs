using System.Text.Json.Serialization;

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
}
