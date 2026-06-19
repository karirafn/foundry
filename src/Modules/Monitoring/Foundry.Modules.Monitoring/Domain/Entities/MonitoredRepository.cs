using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public sealed class MonitoredRepository : AggregateRoot<MonitoredRepositoryId>
{
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

    public DateTimeOffset? LastPolledAt { get; private set; }

    public static MonitoredRepository Create(
        RepositorySlug slug,
        AccountId accountId,
        string host,
        TimeSpan? pollInterval)
    {
        return new MonitoredRepository(MonitoredRepositoryId.New())
        {
            Slug = slug,
            AccountId = accountId,
            Host = host,
            PollInterval = pollInterval,
            IsActive = true,
        };
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

    public void RecordIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
    }
}
