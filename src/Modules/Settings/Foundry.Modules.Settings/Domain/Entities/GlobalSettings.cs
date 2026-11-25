using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.Entities;

public sealed class GlobalSettings : AggregateRoot<GlobalSettingsId>
{
    internal const int MinMaxConcurrent = 1;
    internal const int MaxMaxConcurrent = 20;
    internal const int MinTimeoutMinutes = 1;
    internal const int MaxTimeoutMinutes = 1440;
    internal const int DefaultMaxConcurrent = 1;
    internal const int DefaultTimeoutMinutes = 120;
    internal const int MinProbeIntervalMinutes = 5;
    internal const int MaxProbeIntervalMinutes = 10080;
    internal const int DefaultProbeIntervalMinutes = 60;
    internal const int MinPollIntervalSeconds = 5;
    internal const int MaxPollIntervalSeconds = 3600;
    internal const int DefaultPollIntervalSeconds = 30;
    internal const int MinBackstopIntervalMinutes = 5;
    internal const int MaxBackstopIntervalMinutes = 1440;
    internal const int DefaultBackstopIntervalMinutes = 15;
    internal const int MaxWebhookPublicBaseUrlLength = 2048;
    internal const int MaxProviderHostLength = 255;
    internal const int MaxAllowedProviderHostCount = 50;
    internal const int MinAwaitingInputReminderDelayHours = 1;
    internal const int MaxAwaitingInputReminderDelayHours = 8760;
    internal const int DefaultAwaitingInputReminderDelayHours = 72;

    private GlobalSettings() : base(GlobalSettingsId.Default)
    {
    }

    private GlobalSettings(GlobalSettingsId id, DateTimeOffset createdAt) : base(id)
    {
        MaxConcurrent = DefaultMaxConcurrent;
        TimeoutMinutes = DefaultTimeoutMinutes;
        ProbeIntervalMinutes = DefaultProbeIntervalMinutes;
        PollIntervalSeconds = DefaultPollIntervalSeconds;
        BackstopIntervalMinutes = DefaultBackstopIntervalMinutes;
        AwaitingInputReminderDelayHours = DefaultAwaitingInputReminderDelayHours;
        AutoResumeOnUsageReset = true;
        WorkerImageConfiguration = WorkerImageConfiguration.Default;
        WorkerResourceLimits = WorkerResourceLimits.Default;
        ClaudeCodeSettings = ClaudeCodeSettings.Default;
        ImageBuildState = new ImageBuildState.Idle();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public int MaxConcurrent { get; private set; }

    public int TimeoutMinutes { get; private set; }

    public int ProbeIntervalMinutes { get; private set; }

    public int PollIntervalSeconds { get; private set; }

    /// <summary>
    /// The poll interval for repositories whose webhook is <c>Active</c> (ADR 0085) — the
    /// health-gated backstop that recovers lost webhook nudges.
    /// </summary>
    public int BackstopIntervalMinutes { get; private set; }

    /// <summary>
    /// How long a posted question waits without input before Foundry posts its one reminder in the same thread (ADR 0096).
    /// </summary>
    public int AwaitingInputReminderDelayHours { get; private set; }

    /// <summary>
    /// The operator-supplied public base URL (absolute <c>https</c>) that a tunnel or reverse proxy
    /// forwards to Foundry's webhook-only port. <see langword="null"/> when webhooks are not configured.
    /// </summary>
    public WebhookPublicBaseUrl? WebhookPublicBaseUrl { get; private set; }

    public bool AutoResumeOnUsageReset { get; private set; }

    public WorkerImageConfiguration WorkerImageConfiguration { get; private set; } = null!;

    /// <summary>
    /// Whether full-capability (implement) runs get the nested rootless Docker daemon and its relaxed container
    /// allowances (ADR 0110). An operator switch about privileges, separate from the image: changing it rebuilds nothing.
    /// </summary>
    public bool NestedDockerEnabled { get; private set; }

    public WorkerResourceLimits WorkerResourceLimits { get; private set; } = null!;

    public ClaudeCodeSettings ClaudeCodeSettings { get; private set; } = null!;

    public IReadOnlyList<string> AllowedProviderHosts { get; private set; } = [];

    internal ImageBuildState ImageBuildState { get; private set; } = null!;

    // Set only by a successful build and never cleared, so a later failed build does not hide an image
    // that is still usable. It records a past build, not that the image still exists in Docker.
    public DateTimeOffset? LastImageBuiltAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static GlobalSettings Create()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new GlobalSettings(GlobalSettingsId.Default, now);
    }

    public void UpdateDispatchSettings(bool autoResume)
    {
        AutoResumeOnUsageReset = autoResume;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Result UpdateProbeInterval(int probeIntervalMinutes)
    {
        if (probeIntervalMinutes < MinProbeIntervalMinutes || probeIntervalMinutes > MaxProbeIntervalMinutes)
        {
            return SettingsErrors.InvalidProbeInterval(probeIntervalMinutes);
        }

        ProbeIntervalMinutes = probeIntervalMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }

    public Result UpdateAwaitingInputReminderDelay(int hours)
    {
        if (hours < MinAwaitingInputReminderDelayHours || hours > MaxAwaitingInputReminderDelayHours)
        {
            return SettingsErrors.InvalidAwaitingInputReminderDelay(hours);
        }

        AwaitingInputReminderDelayHours = hours;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }

    public Result UpdatePollInterval(int pollIntervalSeconds)
    {
        if (pollIntervalSeconds < MinPollIntervalSeconds || pollIntervalSeconds > MaxPollIntervalSeconds)
        {
            return SettingsErrors.InvalidPollInterval(pollIntervalSeconds);
        }

        PollIntervalSeconds = pollIntervalSeconds;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }

    /// <summary>
    /// Updates the webhook ingress settings. The public base URL is required (ADR 0116): it can be changed
    /// but never cleared, and must pass <see cref="ValueObjects.WebhookPublicBaseUrl.Create"/>.
    /// </summary>
    public Result UpdateWebhookSettings(string? publicBaseUrl, int backstopIntervalMinutes)
    {
        Result<WebhookPublicBaseUrl> validated = ValidateWebhookSettings(publicBaseUrl, backstopIntervalMinutes);
        if (validated is Result<WebhookPublicBaseUrl>.Failure failure)
        {
            return failure.Error;
        }

        WebhookPublicBaseUrl = ((Result<WebhookPublicBaseUrl>.Success)validated).Value;
        BackstopIntervalMinutes = backstopIntervalMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }

    /// <summary>
    /// Checks webhook settings without applying them, returning the URL they would store, so a caller can
    /// verify that URL before <see cref="UpdateWebhookSettings"/> changes anything.
    /// </summary>
    public static Result<WebhookPublicBaseUrl> ValidateWebhookSettings(string? publicBaseUrl, int backstopIntervalMinutes)
    {
        if (backstopIntervalMinutes < MinBackstopIntervalMinutes || backstopIntervalMinutes > MaxBackstopIntervalMinutes)
        {
            return Result<WebhookPublicBaseUrl>.Fail(SettingsErrors.InvalidBackstopInterval(backstopIntervalMinutes));
        }

        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return Result<WebhookPublicBaseUrl>.Fail(SettingsErrors.WebhookPublicBaseUrlRequired);
        }

        return ValueObjects.WebhookPublicBaseUrl.Create(publicBaseUrl);
    }

    public bool UpdateWorkerImageConfiguration(WorkerImageConfiguration config)
    {
        if (WorkerImageConfiguration == config)
        {
            return false;
        }

        WorkerImageConfiguration = config;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool UpdateNestedDocker(bool enabled)
    {
        if (NestedDockerEnabled == enabled)
        {
            return false;
        }

        NestedDockerEnabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool UpdateWorkerEnvironment(WorkerResourceLimits limits)
    {
        if (WorkerResourceLimits == limits)
        {
            return false;
        }

        WorkerResourceLimits = limits;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool UpdateClaudeCodeSettings(ClaudeCodeSettings settings)
    {
        if (ClaudeCodeSettings == settings)
        {
            return false;
        }

        ClaudeCodeSettings = settings;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public void BeginImageBuild()
    {
        ImageBuildState = new ImageBuildState.Building();
        UpdatedAt = DateTimeOffset.UtcNow;
        AddIntegrationEvent(new ImageBuildStarted());
    }

    public void CompleteImageBuild()
    {
        ImageBuildState = new ImageBuildState.Idle();
        LastImageBuiltAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddIntegrationEvent(new ImageBuildCompleted());
    }

    public void FailImageBuild(string? errorTail, DateTimeOffset? nextRetryAt, int attempt)
    {
        ImageBuildState = new ImageBuildState.Failed(errorTail, nextRetryAt, attempt);
        UpdatedAt = DateTimeOffset.UtcNow;
        AddIntegrationEvent(new ImageBuildFailed(errorTail, nextRetryAt, attempt));
    }

    public void FailScheduledImageBuild(string? errorTail, DateTimeOffset? nextRetryAt, int attempt)
    {
        ImageBuildState = new ImageBuildState.ScheduledRebuildFailed(errorTail, nextRetryAt, attempt);
        UpdatedAt = DateTimeOffset.UtcNow;
        AddIntegrationEvent(new ScheduledImageBuildFailed(errorTail, nextRetryAt, attempt));
    }

    public Result UpdateAllowedProviderHosts(IReadOnlyList<string> hosts)
    {
        List<string> normalized = new(hosts.Count);

        if (hosts.Count > MaxAllowedProviderHostCount)
        {
            return SettingsErrors.TooManyProviderHosts(hosts.Count);
        }

        foreach (string host in hosts)
        {
            string trimmed = (host ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return SettingsErrors.InvalidProviderHost(trimmed);
            }

            if (trimmed.EndsWith('.'))
            {
                return SettingsErrors.InvalidProviderHost(trimmed);
            }

            if (trimmed.Length > MaxProviderHostLength)
            {
                return SettingsErrors.ProviderHostTooLong(trimmed);
            }

            if (trimmed.Contains("://", StringComparison.Ordinal))
            {
                return SettingsErrors.InvalidProviderHost(trimmed);
            }

            if (trimmed.Contains('/', StringComparison.Ordinal))
            {
                return SettingsErrors.InvalidProviderHost(trimmed);
            }

            if (trimmed.Contains(':', StringComparison.Ordinal))
            {
                return SettingsErrors.InvalidProviderHost(trimmed);
            }

            if (trimmed.Any(char.IsWhiteSpace))
            {
                return SettingsErrors.InvalidProviderHost(trimmed);
            }

            normalized.Add(trimmed.ToLowerInvariant());
        }

        AllowedProviderHosts = normalized;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }

    public Result UpdateLimits(int maxConcurrent, int timeoutMinutes)
    {
        if (maxConcurrent < MinMaxConcurrent || maxConcurrent > MaxMaxConcurrent)
        {
            return SettingsErrors.InvalidMaxConcurrent(maxConcurrent);
        }

        if (timeoutMinutes < MinTimeoutMinutes || timeoutMinutes > MaxTimeoutMinutes)
        {
            return SettingsErrors.InvalidTimeout(timeoutMinutes);
        }

        MaxConcurrent = maxConcurrent;
        TimeoutMinutes = timeoutMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }
}
