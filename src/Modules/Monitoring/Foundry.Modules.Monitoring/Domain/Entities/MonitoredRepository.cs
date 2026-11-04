using System.Diagnostics;
using System.Security.Cryptography;

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

    public const int MaxPollIntervalSeconds = 86400;

    /// <summary>
    /// Minimum interval between automatic write-probe re-attempts for a repository parked at
    /// <see cref="WriteProbeVerdict.Unknown"/>. Passed into <see cref="IsDueForWriteProbe"/> by
    /// callers (mirroring how <see cref="IsDueForPoll"/> receives its interval) so the poller and
    /// tests share one source of truth.
    /// </summary>
    internal static readonly TimeSpan WriteProbeCooldown = TimeSpan.FromMinutes(15);

    /// <summary>Length of the opaque webhook hook id (16 random bytes, lower-case hex).</summary>
    public const int WebhookHookIdLength = 32;

    private const int WebhookHookIdBytes = 16;
    private const int WebhookSecretBytes = 32;

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

    /// <summary>
    /// The operator intent, captured when the repository is added, that Foundry may write its
    /// default-branch rules to the provider. Persisted intent only — polling never writes rules.
    /// </summary>
    public bool ApplyBranchRules { get; private set; }

    internal WriteProbeVerdict WriteProbeVerdict { get; private set; } = new WriteProbeVerdict.Unknown();

    /// <summary>
    /// Random opaque identifier addressing this repository's webhook payload URL
    /// (<c>/webhooks/github/{hookId}</c> or <c>/webhooks/gitlab/{hookId}</c>). Carries no information about the repository.
    /// </summary>
    public string WebhookHookId { get; private set; } = NewWebhookHookId();

    /// <summary>
    /// Foundry-generated HMAC secret for webhook deliveries, encrypted at rest. Never returned in list
    /// responses — only from the explicit reveal endpoint. <see langword="null"/> until first generated
    /// (repositories created before webhooks existed) or when it can no longer be decrypted.
    /// </summary>
    internal string? WebhookSecret { get; private set; }

    /// <summary>
    /// The persisted webhook health. <see cref="ValueObjects.WebhookStatus.NotConfigured"/> is never
    /// stored — it is derived by <see cref="EffectiveWebhookStatus"/> when ingress is not configured.
    /// </summary>
    public WebhookStatus WebhookStatus { get; private set; } = WebhookStatus.NotRegistered;

    public static Result<MonitoredRepository> Create(
        RepositorySlug slug,
        string host,
        TimeSpan? pollInterval,
        int position = 0,
        int maxConcurrentWorkers = DefaultMaxConcurrentWorkers,
        bool applyBranchRules = false)
    {
        if (maxConcurrentWorkers < MinMaxConcurrentWorkers || maxConcurrentWorkers > MaxMaxConcurrentWorkers)
        {
            return Result<MonitoredRepository>.Fail(MonitoredRepositoryErrors.InvalidMaxConcurrentWorkers(maxConcurrentWorkers));
        }

        if (pollInterval.HasValue && pollInterval.Value.TotalSeconds <= 0)
        {
            return Result<MonitoredRepository>.Fail(MonitoredRepositoryErrors.PollIntervalNotPositive());
        }

        if (pollInterval.HasValue && pollInterval.Value.TotalSeconds > MaxPollIntervalSeconds)
        {
            return Result<MonitoredRepository>.Fail(MonitoredRepositoryErrors.PollIntervalTooLarge(MaxPollIntervalSeconds));
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
            ApplyBranchRules = applyBranchRules,
            WebhookHookId = NewWebhookHookId(),
            WebhookSecret = NewWebhookSecret(),
            WebhookStatus = WebhookStatus.NotRegistered,
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

    public TimeSpan EffectivePollInterval(TimeSpan defaultInterval) => PollInterval ?? defaultInterval;

    public bool IsDueForPoll(TimeSpan defaultInterval, DateTimeOffset now)
        => IsDueForPoll(defaultInterval, now, backstopInterval: null);

    /// <summary>
    /// Due-for-poll check that honours the health-gated backstop (ADR 0085): while the webhook is
    /// <see cref="ValueObjects.WebhookStatus.Active"/> and a <paramref name="backstopInterval"/> is supplied
    /// (webhook ingress configured), the repository polls at the Backstop Interval — never more often
    /// than its normal effective interval. Every other status polls at the normal effective interval.
    /// </summary>
    public bool IsDueForPoll(TimeSpan defaultInterval, DateTimeOffset now, TimeSpan? backstopInterval)
    {
        if (LastPolledAt is null)
        {
            return true;
        }

        return LastPolledAt.Value + ScheduledPollInterval(defaultInterval, backstopInterval) < now;
    }

    /// <summary>
    /// The interval between scheduled polls: the Backstop Interval while the webhook is
    /// <see cref="ValueObjects.WebhookStatus.Active"/> (and ingress is configured), otherwise
    /// <see cref="EffectivePollInterval"/>.
    /// </summary>
    public TimeSpan ScheduledPollInterval(TimeSpan defaultInterval, TimeSpan? backstopInterval)
    {
        TimeSpan normal = EffectivePollInterval(defaultInterval);

        if (backstopInterval is { } backstop && WebhookStatus == WebhookStatus.Active)
        {
            return backstop > normal ? backstop : normal;
        }

        return normal;
    }

    /// <summary>
    /// The webhook status shown to operators: <see cref="ValueObjects.WebhookStatus.NotConfigured"/> when
    /// webhook ingress is not configured, otherwise the persisted status.
    /// </summary>
    public WebhookStatus EffectiveWebhookStatus(bool ingressConfigured)
        => ingressConfigured ? WebhookStatus : WebhookStatus.NotConfigured;

    /// <summary>
    /// Returns the webhook secret, generating one first when none exists (a repository created before
    /// webhooks existed, or a secret that can no longer be decrypted).
    /// </summary>
    public string EnsureWebhookSecret()
    {
        WebhookSecret ??= NewWebhookSecret();
        return WebhookSecret;
    }

    /// <summary>
    /// Records the operator's "I've added the webhook" action: the hook waits for its first verified
    /// delivery. An already <see cref="ValueObjects.WebhookStatus.Active"/> hook stays active.
    /// </summary>
    public void MarkWebhookRegistered()
    {
        if (WebhookStatus == WebhookStatus.Active)
        {
            return;
        }

        WebhookStatus = WebhookStatus.Pending;
    }

    /// <summary>
    /// Records a verified webhook delivery (including <c>ping</c>). <see cref="ValueObjects.WebhookStatus.Pending"/>
    /// and <see cref="ValueObjects.WebhookStatus.Silent"/> become <see cref="ValueObjects.WebhookStatus.Active"/>.
    /// Returns <see langword="true"/> when the status changed.
    /// </summary>
    public bool RecordWebhookDelivery()
    {
        if (WebhookStatus is not (WebhookStatus.Pending or WebhookStatus.Silent))
        {
            return false;
        }

        WebhookStatus = WebhookStatus.Active;
        return true;
    }

    /// <summary>
    /// Records a missed change — a change found by a scheduled poll, in a category the provider has a
    /// webhook event for, that no delivery announced. Moves <see cref="ValueObjects.WebhookStatus.Active"/>
    /// to <see cref="ValueObjects.WebhookStatus.Silent"/>. Returns <see langword="true"/> when the status changed.
    /// </summary>
    public bool RecordMissedChange()
    {
        if (WebhookStatus != WebhookStatus.Active)
        {
            return false;
        }

        WebhookStatus = WebhookStatus.Silent;
        return true;
    }

    private static string NewWebhookHookId()
        => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(WebhookHookIdBytes));

    private static string NewWebhookSecret()
        => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(WebhookSecretBytes));

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

    /// <summary>
    /// Records an explicit operator opt-in to Foundry writing its default-branch rules — e.g. the
    /// repository card's "Apply branch rules" action, which flips an add-time opt-out.
    /// </summary>
    public void OptInToBranchRules()
    {
        ApplyBranchRules = true;
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
