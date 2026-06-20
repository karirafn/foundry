using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RepositoryMappings
{
    internal static int? ToSeconds(TimeSpan? interval) =>
        interval.HasValue ? (int)interval.Value.TotalSeconds : null;

    internal static RepositoryEligibilityInfo? ToEligibilityInfo(RepositoryEligibility? eligibility) =>
        eligibility switch
        {
            null => null,
            RepositoryEligibility.Eligible => new RepositoryEligibilityInfo("eligible", []),
            RepositoryEligibility.Ineligible ineligible => new RepositoryEligibilityInfo(
                "ineligible",
                ineligible.Violations
                    .Select(v => new EligibilityViolationInfo(v.Rule, v.Description))
                    .ToList()),
            RepositoryEligibility.Unreachable => new RepositoryEligibilityInfo("unreachable", []),
            _ => null,
        };
}
