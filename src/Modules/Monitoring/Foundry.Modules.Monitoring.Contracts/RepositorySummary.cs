namespace Foundry.Modules.Monitoring.Contracts;

public sealed record RepositorySummary(
    Guid Id,
    string Slug,
    Guid AccountId,
    string AccountName,
    string ProviderType,
    int? PollIntervalSeconds,
    bool IsActive,
    int Position,
    DateTimeOffset? LastPolledAt = null,
    RepositoryEligibilityInfo? Eligibility = null,
    DateTimeOffset? UntrackSuppressedSince = null);
