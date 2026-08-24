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

    public TimeSpan? PollInterval { get; private set; }

    public bool IsActive { get; private set; }

    public int Position { get; private set; }

    public DateTimeOffset? LastPolledAt { get; private set; }

    public DateTimeOffset? UntrackSuppressedSince { get; private set; }

    public RepositoryEligibility? Eligibility { get; private set; }

    public string? EligibilityStatus { get; private set; }

    internal WriteProbeVerdict WriteProbeVerdict { get; private set; } = new WriteProbeVerdict.Unknown();

    public static MonitoredRepository Create(
        RepositorySlug slug,
        string host,
        TimeSpan? pollInterval,
        int position = 0)
    {
        return new MonitoredRepository(MonitoredRepositoryId.New())
        {
            Slug = slug,
            Host = host,
            PollInterval = pollInterval,
            IsActive = true,
            Position = position,
            Eligibility = new RepositoryEligibility.Unreachable(),
            EligibilityStatus = UnreachableStatus,
            WriteProbeVerdict = new WriteProbeVerdict.Unknown(),
        };
    }

    public void SetPosition(int position)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be non-negative.");
        }

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

    /// <summary>
    /// Marks untrack-pass suppression as active, recording when it first began.
    /// Returns true if this is the null→set transition (first suppression); false if already suppressed.
    /// </summary>
    public bool SuppressUntracking(DateTimeOffset now)
    {
        if (UntrackSuppressedSince is not null)
        {
            return false;
        }

        UntrackSuppressedSince = now;
        return true;
    }

    /// <summary>
    /// Clears untrack-pass suppression. Idempotent — no-op when not suppressed.
    /// </summary>
    public void ClearUntrackSuppression()
    {
        UntrackSuppressedSince = null;
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

    internal void SetWriteProbeVerdict(WriteProbeVerdict verdict)
    {
        WriteProbeVerdict = verdict;
    }

    public void RecordIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
    }
}
