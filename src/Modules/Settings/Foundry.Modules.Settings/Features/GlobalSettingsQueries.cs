using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal sealed class GlobalSettingsQueries(DbContext dbContext) : IGlobalSettingsQueries
{
    public async Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return settings is null ? null : GlobalSettingsMapper.ToSummary(settings);
    }

    public async Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
    {
        int? value = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => (int?)s.MaxConcurrent)
            .FirstOrDefaultAsync(cancellationToken);

        return value ?? GlobalSettings.DefaultMaxConcurrent;
    }

    public async Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
    {
        int? value = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => (int?)s.TimeoutMinutes)
            .FirstOrDefaultAsync(cancellationToken);

        return value ?? GlobalSettings.DefaultTimeoutMinutes;
    }

    public async Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
    {
        int? value = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => (int?)s.ProbeIntervalMinutes)
            .FirstOrDefaultAsync(cancellationToken);

        return value ?? GlobalSettings.DefaultProbeIntervalMinutes;
    }

    public async Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken)
    {
        int? value = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => (int?)s.PollIntervalSeconds)
            .FirstOrDefaultAsync(cancellationToken);

        return value ?? GlobalSettings.DefaultPollIntervalSeconds;
    }

    public async Task<TimeSpan> GetAwaitingInputReminderDelayAsync(CancellationToken cancellationToken)
    {
        int? hours = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => (int?)s.AwaitingInputReminderDelayHours)
            .FirstOrDefaultAsync(cancellationToken);

        return TimeSpan.FromHours(hours ?? GlobalSettings.DefaultAwaitingInputReminderDelayHours);
    }

    public async Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
    {
        var row = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => new
            {
                s.UsageLimitResetsAt,
                s.IsDispatchPaused,
                s.AutoResumeOnUsageReset,
                s.HostPauses,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new DispatchPauseState(null, false, true, []);
        }

        return new DispatchPauseState(
            row.UsageLimitResetsAt,
            row.IsDispatchPaused,
            row.AutoResumeOnUsageReset,
            [.. row.HostPauses.Select(HostPauseMapper.ToPausedHost)]);
    }

    public async Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            return ImageBuildStatus.Idle;
        }

        return settings.ImageBuildState.ToStatus();
    }

    public async Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken)
    {
        // WorkerImageConfiguration is stored as a JSON blob and cannot be projected
        // into a SQL column — the full entity must be loaded and the value read in memory.
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return settings?.WorkerImageConfiguration.ImageTag ?? WorkerImageConfiguration.Default.ImageTag;
    }

    public async Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken)
    {
        // WorkerImageConfiguration is stored as a JSON blob and cannot be projected
        // into a SQL column — the full entity must be loaded and the value read in memory.
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return settings?.WorkerImageConfiguration.BuildEnabled ?? WorkerImageConfiguration.Default.BuildEnabled;
    }

    public async Task<bool> GetNestedDockerEnabledAsync(CancellationToken cancellationToken)
    {
        bool? value = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => (bool?)s.NestedDockerEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        return value ?? false;
    }

    public async Task<WorkerResourceLimitsSnapshot> GetWorkerResourceLimitsAsync(CancellationToken cancellationToken)
    {
        // WorkerResourceLimits is stored as a JSON blob and cannot be projected
        // into a SQL column — the full entity must be loaded and the value read in memory.
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        WorkerResourceLimits limits = settings?.WorkerResourceLimits ?? WorkerResourceLimits.Default;
        return new WorkerResourceLimitsSnapshot(limits.MemoryLimitMb, limits.CpuLimit, limits.PidsLimit);
    }

    public async Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken)
    {
        // ClaudeCodeSettings is stored as a JSON blob and cannot be projected
        // into a SQL column — the full entity must be loaded and the value read in memory.
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        ClaudeCodeSettings claudeCode = settings?.ClaudeCodeSettings ?? ClaudeCodeSettings.Default;
        return new ClaudeCodeSettingsSnapshot(claudeCode.AdditionalDenyRules);
    }

    public async Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
    {
        // AllowedProviderHosts is stored as a JSON blob and cannot be projected
        // into a SQL column — the full entity must be loaded and the value read in memory.
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return settings?.AllowedProviderHosts ?? [];
    }
}
