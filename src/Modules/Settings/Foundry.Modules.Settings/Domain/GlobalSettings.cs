using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain;

public sealed class GlobalSettings : AggregateRoot<GlobalSettingsId>
{
    internal const int MinMaxConcurrent = 1;
    internal const int MaxMaxConcurrent = 20;
    internal const int MinTimeoutMinutes = 1;
    internal const int MaxTimeoutMinutes = 1440;
    internal const int DefaultMaxConcurrent = 1;
    internal const int DefaultTimeoutMinutes = 120;
    internal const int MaxPromptTemplateLength = 32768;
    internal const int MinDefaultCooldownMinutes = 1;
    internal const int MaxDefaultCooldownMinutes = 1440;
    internal const int DefaultCooldownMinutesValue = 60;
    internal const int MaxUsageLimitResetDays = 7;

    private GlobalSettings() : base(GlobalSettingsId.Default)
    {
    }

    private GlobalSettings(GlobalSettingsId id, DateTimeOffset createdAt) : base(id)
    {
        AuthMode = new AuthMode.ApiKey(string.Empty);
        MaxConcurrent = DefaultMaxConcurrent;
        TimeoutMinutes = DefaultTimeoutMinutes;
        AutoResumeOnUsageReset = true;
        DefaultCooldownMinutes = DefaultCooldownMinutesValue;
        WorkerImageConfiguration = WorkerImageConfiguration.Default;
        ImageBuildState = new ImageBuildState.Idle();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public AuthMode AuthMode { get; private set; } = null!;

    public int MaxConcurrent { get; private set; }

    public int TimeoutMinutes { get; private set; }

    public string? SystemPromptTemplate { get; private set; }

    public string? WorkerPromptTemplate { get; private set; }

    public DateTimeOffset? UsageLimitResetsAt { get; private set; }

    public bool IsDispatchPaused { get; private set; }

    public bool AutoResumeOnUsageReset { get; private set; }

    public int DefaultCooldownMinutes { get; private set; }

    public WorkerImageConfiguration WorkerImageConfiguration { get; private set; } = null!;

    public ImageBuildState ImageBuildState { get; private set; } = null!;

    public DateTimeOffset? LastImageBuiltAt { get; private set; }

    public bool AuthInvalidPause { get; private set; }

    public string? OAuthAccountEmail { get; private set; }

    public string? OAuthAccountOrgName { get; private set; }

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

    public void ResumeDispatch()
    {
        IsDispatchPaused = false;
        UsageLimitResetsAt = null;
        AuthInvalidPause = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void PauseForAuthInvalid()
    {
        AuthInvalidPause = true;
        UpdatedAt = DateTimeOffset.UtcNow;
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

    public Result UpdateDispatchSettings(bool autoResume, int defaultCooldownMinutes)
    {
        if (defaultCooldownMinutes < MinDefaultCooldownMinutes || defaultCooldownMinutes > MaxDefaultCooldownMinutes)
        {
            return SettingsErrors.InvalidDefaultCooldown(defaultCooldownMinutes);
        }

        AutoResumeOnUsageReset = autoResume;
        DefaultCooldownMinutes = defaultCooldownMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }

    public void SetAuthMode(AuthMode mode)
    {
        AuthMode = mode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetOAuthAccountIdentity(string? email, string? orgName, string? subscriptionType)
    {
        OAuthAccountEmail = email;
        OAuthAccountOrgName = orgName;
        AuthMode = new AuthMode.OAuth(subscriptionType);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Result UpdatePromptTemplates(string? systemPromptTemplate, string? workerPromptTemplate)
    {
        if (systemPromptTemplate is not null && systemPromptTemplate.Length == 0)
        {
            return SettingsErrors.InvalidPromptTemplate;
        }

        if (workerPromptTemplate is not null && workerPromptTemplate.Length == 0)
        {
            return SettingsErrors.InvalidPromptTemplate;
        }

        if (systemPromptTemplate is not null && systemPromptTemplate.Length > MaxPromptTemplateLength)
        {
            return SettingsErrors.InvalidPromptTemplateTooLong;
        }

        if (workerPromptTemplate is not null && workerPromptTemplate.Length > MaxPromptTemplateLength)
        {
            return SettingsErrors.InvalidPromptTemplateTooLong;
        }

        SystemPromptTemplate = systemPromptTemplate;
        WorkerPromptTemplate = workerPromptTemplate;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
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

    public void BeginImageBuild()
    {
        ImageBuildState = new ImageBuildState.Building();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void CompleteImageBuild()
    {
        ImageBuildState = new ImageBuildState.Idle();
        LastImageBuiltAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void FailImageBuild(string? errorTail)
    {
        ImageBuildState = new ImageBuildState.Failed(errorTail);
        UpdatedAt = DateTimeOffset.UtcNow;
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
