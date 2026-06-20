namespace Foundry.Modules.Monitoring.Contracts;

public sealed record RepositoryEligibilityInfo(
    string Status,
    IReadOnlyList<EligibilityViolationInfo> Violations);
