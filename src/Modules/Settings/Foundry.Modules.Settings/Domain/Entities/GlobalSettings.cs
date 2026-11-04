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
    internal const int MaxUsageLimitResetDays = 7;
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
        AutoResumeOnUsageReset = true;
        WorkerImageConfiguration = WorkerImageConfiguration.Default;
        WorkerResourceLimits = WorkerResourceLimits.Default;
        WorkerMounts = WorkerMounts.Default;
        ClaudeCodeSettings = ClaudeCodeSettings.Default;
        HostHooks = HostHooks.Default;
        WorkerPrompts = WorkerPrompts.Default;
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
    /// The operator-supplied public base URL (absolute <c>https</c>) that a tunnel or reverse proxy
    /// forwards to Foundry's webhook-only port. <see langword="null"/> when webhooks are not configured.
    /// </summary>
    public string? WebhookPublicBaseUrl { get; private set; }

    public WorkerPrompts WorkerPrompts { get; private set; } = null!;

    public DateTimeOffset? UsageLimitResetsAt { get; private set; }

    public bool IsDispatchPaused { get; private set; }

    public bool AutoResumeOnUsageReset { get; private set; }

    public WorkerImageConfiguration WorkerImageConfiguration { get; private set; } = null!;

    public WorkerResourceLimits WorkerResourceLimits { get; private set; } = null!;

    public WorkerMounts WorkerMounts { get; private set; } = null!;

    public ClaudeCodeSettings ClaudeCodeSettings { get; private set; } = null!;

    public HostHooks HostHooks { get; private set; } = null!;

    public IReadOnlyList<string> AllowedProviderHosts { get; private set; } = [];

    internal ImageBuildState ImageBuildState { get; private set; } = null!;

    public DateTimeOffset? LastImageBuiltAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static GlobalSettings Create()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new GlobalSettings(GlobalSettingsId.Default, now);
    }

    public void PauseDispatch()
    {
        IsDispatchPaused = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Clears every dispatch-pause reason — the operator's manual pause and the usage-limit
    /// pause — for the manual "Resume All" action.
    /// </summary>
    public void ResumeAllDispatch()
    {
        IsDispatchPaused = false;
        UsageLimitResetsAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Clears the usage-limit pause reason only. The operator's manual dispatch pause
    /// (<see cref="IsDispatchPaused"/>) is left untouched, so a manual pause survives a
    /// usage-limit auto-resume. Returns <c>true</c> when dispatch is now fully unpaused
    /// (no manual pause remains), <c>false</c> when a manual pause still holds dispatch.
    /// </summary>
    public bool ClearUsageLimitPause()
    {
        UsageLimitResetsAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        return !IsDispatchPaused;
    }

    public void SetUsageLimitResetsAt(DateTimeOffset resetsAt)
    {
        if (resetsAt <= DateTimeOffset.UtcNow)
        {
            return;
        }

        DateTimeOffset maxResetTime = DateTimeOffset.UtcNow.AddDays(MaxUsageLimitResetDays);
        DateTimeOffset clampedResetsAt = resetsAt > maxResetTime ? maxResetTime : resetsAt;

        UsageLimitResetsAt = UsageLimitResetsAt.HasValue && UsageLimitResetsAt.Value > clampedResetsAt
            ? UsageLimitResetsAt
            : clampedResetsAt;

        UpdatedAt = DateTimeOffset.UtcNow;
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
    /// Updates the webhook ingress settings. The public base URL is optional; when present it must be
    /// an absolute <c>https</c> URL without query or fragment. A trailing slash is trimmed so the payload
    /// URL is built as <c>&lt;base&gt;/webhooks/github/&lt;hookId&gt;</c>.
    /// </summary>
    public Result UpdateWebhookSettings(string? publicBaseUrl, int backstopIntervalMinutes)
    {
        if (backstopIntervalMinutes < MinBackstopIntervalMinutes || backstopIntervalMinutes > MaxBackstopIntervalMinutes)
        {
            return SettingsErrors.InvalidBackstopInterval(backstopIntervalMinutes);
        }

        string? normalizedUrl = null;
        if (!string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            string trimmed = publicBaseUrl.Trim();

            if (trimmed.Length > MaxWebhookPublicBaseUrlLength
                || !Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || string.IsNullOrEmpty(uri.Host))
            {
                return SettingsErrors.InvalidWebhookPublicBaseUrl;
            }

            normalizedUrl = trimmed.TrimEnd('/');
        }

        WebhookPublicBaseUrl = normalizedUrl;
        BackstopIntervalMinutes = backstopIntervalMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }

    public void UpdatePrompts(WorkerPrompts prompts)
    {
        WorkerPrompts = prompts;
        UpdatedAt = DateTimeOffset.UtcNow;
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

    public bool UpdateWorkerEnvironment(WorkerResourceLimits limits, WorkerMounts mounts)
    {
        if (WorkerResourceLimits == limits && WorkerMounts == mounts)
        {
            return false;
        }

        WorkerResourceLimits = limits;
        WorkerMounts = mounts;
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

    public bool UpdateHostHooks(HostHooks hostHooks)
    {
        if (HostHooks == hostHooks)
        {
            return false;
        }

        HostHooks = hostHooks;
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
