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

    public const int MinMaxConcurrentWorkers = 1;
    public const int MaxMaxConcurrentWorkers = 20;
    public const int DefaultMaxConcurrentWorkers = 1;

    // The validators in CreateRepository and UpdateRepository carry their own copy until step 3
    // consolidates them here. This constant governs aggregate-level validation only.
    public const int MaxPollIntervalSeconds = 86400;

    /// <summary>
    /// Minimum interval between automatic write-probe re-attempts for a repository parked at
    /// <see cref="WriteProbeVerdict.Unknown"/>. Passed into <see cref="IsDueForWriteProbe"/> by
    /// callers (mirroring how <see cref="IsDueForPoll"/> receives its interval) so the poller and
    /// tests share one source of truth.
    /// </summary>
    internal static readonly TimeSpan WriteProbeCooldown = TimeSpan.FromMinutes(15);

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

    public int MaxConcurrentWorkers { get; private set; } = DefaultMaxConcurrentWorkers;

    public int Position { get; private set; }

    public DateTimeOffset? LastPolledAt { get; private set; }

    public DateTimeOffset? UntrackSuppressedSince { get; private set; }

    public RepositoryEligibility? Eligibility { get; private set; }

    public string? EligibilityStatus { get; private set; }

    internal WriteProbeVerdict WriteProbeVerdict { get; private set; } = new WriteProbeVerdict.Unknown();

    public static Result<MonitoredRepository> Create(
        RepositorySlug slug,
        string host,
        TimeSpan? pollInterval,
        int position = 0,
        int maxConcurrentWorkers = DefaultMaxConcurrentWorkers)
    {
        if (maxConcurrentWorkers < MinMaxConcurrentWorkers || maxConcurrentWorkers > MaxMaxConcurrentWorkers)
        {
            return Result<MonitoredRepository>.Fail(MonitoredRepositoryErrors.InvalidMaxConcurrentWorkers(maxConcurrentWorkers));
        }

        return new MonitoredRepository(MonitoredRepositoryId.New())
        {
            Slug = slug,
            Host = host,
            PollInterval = pollInterval,
            IsActive = true,
            MaxConcurrentWorkers = maxConcurrentWorkers,
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

    /// <summary>
    /// Returns <see langword="true"/> when the repository should have its write probe re-run on the next
    /// poll cycle: the stored verdict is <see cref="WriteProbeVerdict.Unknown"/> and either the probe
    /// was never attempted (<see cref="WriteProbeVerdict.Unknown.LastAttemptedAt"/> is <see langword="null"/>)
    /// or <paramref name="cooldown"/> has elapsed since the last attempt (strict &lt; boundary, mirroring
    /// <see cref="IsDueForPoll"/>). Returns <see langword="false"/> for <see cref="WriteProbeVerdict.Granted"/>
    /// and <see cref="WriteProbeVerdict.Denied"/> unconditionally — those verdicts are event-triggered.
    /// </summary>
    public bool IsDueForWriteProbe(TimeSpan cooldown, DateTimeOffset now)
    {
        if (WriteProbeVerdict is not WriteProbeVerdict.Unknown unknown)
        {
            return false;
        }

        if (unknown.LastAttemptedAt is null)
        {
            return true;
        }

        return unknown.LastAttemptedAt.Value + cooldown < now;
    }

    public Result Update(TimeSpan? pollInterval, bool isActive, int maxConcurrentWorkers)
    {
        if (maxConcurrentWorkers < MinMaxConcurrentWorkers || maxConcurrentWorkers > MaxMaxConcurrentWorkers)
        {
            return MonitoredRepositoryErrors.InvalidMaxConcurrentWorkers(maxConcurrentWorkers);
        }

        if (pollInterval.HasValue && pollInterval.Value.TotalSeconds <= 0)
        {
            return MonitoredRepositoryErrors.PollIntervalNotPositive();
        }

        if (pollInterval.HasValue && pollInterval.Value.TotalSeconds > MaxPollIntervalSeconds)
        {
            return MonitoredRepositoryErrors.PollIntervalTooLarge(MaxPollIntervalSeconds);
        }

        PollInterval = pollInterval;
        IsActive = isActive;
        MaxConcurrentWorkers = maxConcurrentWorkers;
        return Result.Ok();
    }

    public void MarkPolled(DateTimeOffset polledAt)
    {
        LastPolledAt = polledAt;
    }

    /// <summary>
    /// Marks untrack-pass suppression as active, recording when it first began.
    /// Returns true if this is the null→set transition (first suppression); false if already suppressed.
    /// </summary>
    internal bool SuppressUntracking(DateTimeOffset now)
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
    internal void ClearUntrackSuppression()
    {
        if (UntrackSuppressedSince is null)
        {
            return;
        }

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
