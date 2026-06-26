using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public sealed class MonitoredRepository : AggregateRoot<MonitoredRepositoryId>
{
    private const string EligibleStatus = "eligible";
    private const string IneligibleStatus = "ineligible";
    private const string UnreachableStatus = "unreachable";

    // Private parameterless constructor for EF Core materialization.
    private MonitoredRepository() : base(MonitoredRepositoryId.New())
    {
    }

    private MonitoredRepository(MonitoredRepositoryId id) : base(id)
    {
    }

    public RepositorySlug Slug { get; private set; } = null!;

    public string Host { get; private set; } = string.Empty;

    public AccountId AccountId { get; private set; }

    public TimeSpan? PollInterval { get; private set; }

    public bool IsActive { get; private set; }

    public int Position { get; private set; }

    public DateTimeOffset? LastPolledAt { get; private set; }

    public RepositoryEligibility? Eligibility { get; private set; }

    public string? EligibilityStatus { get; private set; }

    public static MonitoredRepository Create(
        RepositorySlug slug,
        AccountId accountId,
        string host,
        TimeSpan? pollInterval,
        int position = 0)
    {
        return new MonitoredRepository(MonitoredRepositoryId.New())
        {
            Slug = slug,
            AccountId = accountId,
            Host = host,
            PollInterval = pollInterval,
            IsActive = true,
            Position = position,
            Eligibility = new RepositoryEligibility.Unreachable(),
            EligibilityStatus = UnreachableStatus,
        };
    }

    public void SetPosition(int position)
    {
        Position = position;
    }

    public bool IsDueForPoll(TimeSpan defaultInterval, DateTimeOffset now)
    {
        if (LastPolledAt is null)
        {
            return true;
        }

        TimeSpan effectiveInterval = PollInterval ?? defaultInterval;
        return LastPolledAt.Value + effectiveInterval < now;
    }

    public void Update(TimeSpan? pollInterval, bool isActive)
    {
        PollInterval = pollInterval;
        IsActive = isActive;
    }

    public void MarkPolled(DateTimeOffset polledAt)
    {
        LastPolledAt = polledAt;
    }

    public void SetEligibility(RepositoryEligibility eligibility)
    {
        Eligibility = eligibility;
        EligibilityStatus = eligibility switch
        {
            RepositoryEligibility.Eligible => EligibleStatus,
            RepositoryEligibility.Ineligible => IneligibleStatus,
            RepositoryEligibility.Unreachable => UnreachableStatus,
            _ => throw new UnreachableException($"Unhandled eligibility variant: {eligibility.GetType().Name}"),
        };
    }

    public void RecordIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
    }
}
