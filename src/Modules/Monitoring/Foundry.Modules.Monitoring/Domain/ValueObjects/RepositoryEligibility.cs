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

    /// <summary>
    /// The repository's eligibility cannot be determined — for example, the write probe failed or
    /// branch-rule data was unavailable. <see cref="Reason"/> records the specific cause so the
    /// API surface (and future UI) can surface a meaningful message.
    /// Legacy rows persisted without a <c>Reason</c> field deserialize to
    /// <see cref="UnreachableReason.NeverProbed"/> (the initial/safe default).
    /// </summary>
    public sealed record Unreachable : RepositoryEligibility
    {
        public UnreachableReason Reason { get; }

        // Internal — mirrors the Ineligible pattern so STJ polymorphic deserialization can
        // instantiate the type. The [JsonConstructor] attribute is required because the record
        // has an explicit constructor and STJ would otherwise attempt the parameterless path.
        [JsonConstructor]
        internal Unreachable(UnreachableReason reason = UnreachableReason.NeverProbed)
        {
            Reason = reason;
        }

        // Parameterless constructor for callers that do not yet supply a reason.
        // Defaults to NeverProbed so existing call sites continue to compile unchanged.
        public Unreachable() : this(UnreachableReason.NeverProbed) { }
    }
}
