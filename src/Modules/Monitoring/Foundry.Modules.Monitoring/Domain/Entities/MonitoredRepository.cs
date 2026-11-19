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
    /// <see cref="WriteProbeVerdict.Unknown"/>. <see cref="IsDueForWriteProbe"/> picks it from the verdict.
    /// </summary>
    internal static readonly TimeSpan WriteProbeCooldown = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Minimum interval between automatic write-probe re-attempts for a repository parked at
    /// <see cref="WriteProbeVerdict.Denied"/>. Longer than <see cref="WriteProbeCooldown"/> because a
    /// denied probe is a definitive answer and each GitHub probe sequence spends write POSTs against
    /// the secondary rate limit; it exists at all because a permission gained in place on the
    /// provider raises no credential event (ADR 0115).
    /// </summary>
    internal static readonly TimeSpan DeniedWriteProbeCooldown = TimeSpan.FromHours(1);

    /// <summary>
    /// Minimum wait after a failed webhook registration before the scheduled poll tries again, so a
    /// permanently failing repository spends at most one provider attempt per hour.
    /// </summary>
    internal static readonly TimeSpan WebhookRegistrationRetryDelay = TimeSpan.FromHours(1);

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

    /// <summary>
    /// Whether Foundry rebases a worker branch onto the default branch through the provider after each run
    /// that leaves an open pull request (ADR 0087). Read when the rebase step runs, not snapshotted.
    /// Defaults to <see langword="true"/>: the rebase writes only Foundry-owned branches.
    /// </summary>
    public bool RebaseAfterRun { get; private set; } = true;

    /// <summary>
    /// Whether Foundry merges the pull request it opened itself once its checks gate resolves <c>Passed</c>,
    /// pinned to the head the gate verified (ADR 0105). Read when the checks verdict is recorded and when the merge
    /// step runs, never snapshotted. Defaults to <see langword="false"/>: opting in is a trust decision about one
    /// repository's CI. Not the provider's native auto-merge, which Foundry never arms.
    /// </summary>
    public bool MergeWhenChecksPass { get; private set; }

    /// <summary>
    /// Whether a pull request whose checks gate resolves green goes through the review role before Foundry merges it
    /// or hands it to a person (ADR 0113). Read when the checks verdict is recorded, never snapshotted. Defaults to
    /// <see langword="false"/> while the review step ships dark; nothing sets it yet.
    /// </summary>
    public bool ReviewPullRequests { get; private set; }

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

    /// <summary>
    /// The <c>WebhookSpecStamp</c> of the hook settings and public base URL the webhook was last registered
    /// with by Foundry. <see langword="null"/> until Foundry registers it; a stamp that differs from the
    /// current one makes the repository due for registration (<see cref="IsDueForWebhookRegistration"/>).
    /// </summary>
    internal string? WebhookSpecStamp { get; private set; }

    /// <summary>When the last registration attempt failed; cleared by a successful registration.</summary>
    internal DateTimeOffset? WebhookRegistrationFailedAt { get; private set; }

    /// <summary>
    /// The Package Sources the repository's root <c>nuget.config</c> on its default branch declares, as last read
    /// by the poll. Empty when it has no root config or the config is invalid.
    /// </summary>
    internal IReadOnlyList<DeclaredPackageSource> DeclaredPackageSources { get; private set; } = [];

    /// <summary>The raw text of the root <c>nuget.config</c> last read, kept to generate a worker's config from.</summary>
    internal string? RootNuGetConfigText { get; private set; }

    /// <summary>The blob SHA of the root <c>nuget.config</c> last read; an unchanged SHA is not read again.</summary>
    internal string? RootNuGetConfigBlobSha { get; private set; }

    /// <summary>Why the root <c>nuget.config</c> could not be read as a source list, or null when it could.</summary>
    internal string? RootNuGetConfigInvalidReason { get; private set; }

    /// <summary>The ETag of the last root directory listing whose state was recorded (GitHub only).</summary>
    internal string? RootListingETag { get; private set; }

    /// <summary>The root <c>nuget.config</c> state, composed from the stored columns.</summary>
    internal RootNuGetConfig RootNuGetConfig =>
        (RootNuGetConfigInvalidReason, RootNuGetConfigBlobSha, RootNuGetConfigText) switch
        {
            ({ } reason, _, _) => new RootNuGetConfig.Invalid(RootNuGetConfigBlobSha, RootNuGetConfigText, reason),
            (null, { } sha, { } text) => new RootNuGetConfig.Declared(sha, text, DeclaredPackageSources),
            _ => new RootNuGetConfig.Absent(),
        };

    /// <summary>
    /// Whether the root <c>nuget.config</c> declares at least one Package Source, so the repository's workers restore
    /// through the Package Source Proxy.
    /// </summary>
    internal bool DeclaresPackageSources => RootNuGetConfig is RootNuGetConfig.Declared { Sources.Count: > 0 };

    public static Result<MonitoredRepository> Create(
        RepositorySlug slug,
        string host,
        TimeSpan? pollInterval,
        int position = 0,
        int maxConcurrentWorkers = DefaultMaxConcurrentWorkers,
        bool applyBranchRules = false,
        bool rebaseAfterRun = true,
        bool mergeWhenChecksPass = false)
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
            RebaseAfterRun = rebaseAfterRun,
            MergeWhenChecksPass = mergeWhenChecksPass,
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
    /// Records a successful registration by Foundry with the hook spec <paramref name="specStamp"/>
    /// describes: the hook waits for its first verified delivery, and an earlier failure is cleared. An
    /// already <see cref="ValueObjects.WebhookStatus.Active"/> hook stays active.
    /// </summary>
    public void MarkWebhookRegistered(string specStamp)
    {
        WebhookSpecStamp = specStamp;
        WebhookRegistrationFailedAt = null;
        MarkWebhookPending();
    }

    /// <summary>
    /// Records the operator's "I've added the webhook" action: the hook waits for its first verified
    /// delivery. No stamp is stored, because Foundry does not know the spec of a hook added by hand, so
    /// the scheduled poll still registers it.
    /// </summary>
    public void MarkWebhookAddedManually() => MarkWebhookPending();

    /// <summary>
    /// Records a failed registration attempt at <paramref name="attemptedAt"/>. The stamp and status are
    /// unchanged.
    /// </summary>
    public void RecordWebhookRegistrationFailed(DateTimeOffset attemptedAt) =>
        WebhookRegistrationFailedAt = attemptedAt;

    /// <summary>
    /// Whether the scheduled poll should register the webhook: its stamp differs from
    /// <paramref name="currentStamp"/> (never registered counts as different), the write probe has not
    /// denied the token, and no registration failed within <see cref="WebhookRegistrationRetryDelay"/> —
    /// a strict &lt; boundary, mirroring <see cref="IsDueForWriteProbe"/>.
    /// </summary>
    public bool IsDueForWebhookRegistration(string currentStamp, DateTimeOffset now)
    {
        if (string.Equals(WebhookSpecStamp, currentStamp, StringComparison.Ordinal))
        {
            return false;
        }

        if (WriteProbeVerdict is WriteProbeVerdict.Denied)
        {
            return false;
        }

        return HasCooldownElapsed(WebhookRegistrationFailedAt, WebhookRegistrationRetryDelay, now);
    }

    private void MarkWebhookPending()
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
    /// poll cycle. Any verdict, <see cref="WriteProbeVerdict.Granted"/> included, whose
    /// <see cref="WriteProbeVerdict.PermissionListVersion"/> is not in <paramref name="currentPermissionListVersions"/>
    /// is due at once: it was checked against an older permission list. Otherwise, for
    /// <see cref="WriteProbeVerdict.Unknown"/> that is when the probe was never attempted
    /// or <see cref="WriteProbeCooldown"/> has elapsed since <see cref="WriteProbeVerdict.Unknown.LastAttemptedAt"/>;
    /// for <see cref="WriteProbeVerdict.Denied"/> when <see cref="WriteProbeVerdict.Denied.ProbedAt"/> is
    /// unknown or <see cref="DeniedWriteProbeCooldown"/> has elapsed since it. Both use a strict &lt; boundary,
    /// mirroring <see cref="IsDueForPoll"/>. A current <see cref="WriteProbeVerdict.Granted"/> stays
    /// event-triggered so steady-state polling sends no write probes.
    /// </summary>
    public bool IsDueForWriteProbe(DateTimeOffset now, IReadOnlySet<string> currentPermissionListVersions)
    {
        if (WriteProbeVerdict.PermissionListVersion is not { } version
            || !currentPermissionListVersions.Contains(version))
        {
            return true;
        }

        return WriteProbeVerdict switch
        {
            WriteProbeVerdict.Unknown unknown => HasCooldownElapsed(unknown.LastAttemptedAt, WriteProbeCooldown, now),
            WriteProbeVerdict.Denied denied => HasCooldownElapsed(denied.ProbedAt, DeniedWriteProbeCooldown, now),
            _ => false,
        };
    }

    private static bool HasCooldownElapsed(DateTimeOffset? lastProbedAt, TimeSpan cooldown, DateTimeOffset now)
        => lastProbedAt is null || lastProbedAt.Value + cooldown < now;

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

    /// <summary>Sets the operator toggle that turns the post-run rebase on or off (ADR 0087).</summary>
    public void SetRebaseAfterRun(bool rebaseAfterRun)
    {
        RebaseAfterRun = rebaseAfterRun;
    }

    /// <summary>Sets the operator opt-in for Foundry merging its pull requests when checks pass (ADR 0105).</summary>
    public void SetMergeWhenChecksPass(bool mergeWhenChecksPass)
    {
        MergeWhenChecksPass = mergeWhenChecksPass;
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

    /// <summary>
    /// Records a root directory listing: replaces the root <c>nuget.config</c> state with
    /// <paramref name="config"/> and stores <paramref name="listingETag"/> for the next conditional listing.
    /// </summary>
    internal void RecordRootListing(RootNuGetConfig config, string? listingETag)
    {
        switch (config)
        {
            case RootNuGetConfig.Absent:
                ReplaceRootNuGetConfig([], text: null, blobSha: null, invalidReason: null);
                break;
            case RootNuGetConfig.Declared declared:
                ReplaceRootNuGetConfig([.. declared.Sources], declared.RawText, declared.BlobSha, invalidReason: null);
                break;
            case RootNuGetConfig.Invalid invalid:
                ReplaceRootNuGetConfig([], invalid.RawText, invalid.BlobSha, invalid.Message);
                break;
            default:
                throw new UnreachableException($"Unhandled root nuget.config variant: {config.GetType().Name}");
        }

        RootListingETag = listingETag;
    }

    private void ReplaceRootNuGetConfig(
        IReadOnlyList<DeclaredPackageSource> sources,
        string? text,
        string? blobSha,
        string? invalidReason)
    {
        DeclaredPackageSources = sources;
        RootNuGetConfigText = text;
        RootNuGetConfigBlobSha = blobSha;
        RootNuGetConfigInvalidReason = invalidReason;
    }

    public void RecordIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
    }
}
