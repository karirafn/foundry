namespace Foundry.Modules.Monitoring.Contracts;

public sealed record RepositorySummary(
    Guid Id,
    string Slug,
    Guid AccountId,
    string AccountName,
    string ProviderType,
    int? PollIntervalSeconds,
    bool IsActive,
    DateTimeOffset? LastPolledAt,
    RepositoryEligibilityInfo? Eligibility);
