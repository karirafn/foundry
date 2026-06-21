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

        // Internal — construction is restricted to within this assembly and to System.Text.Json
        // polymorphic deserialization (which requires a reachable [JsonConstructor] to instantiate
        // the type). The non-empty-violations invariant is enforced here at construction time.
        [JsonConstructor]
        internal Ineligible(IReadOnlyList<EligibilityViolation> violations)
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
