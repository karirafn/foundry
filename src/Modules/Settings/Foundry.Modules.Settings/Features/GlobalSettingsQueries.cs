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

    public async Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
        CancellationToken cancellationToken)
    {
        (string? SystemPromptTemplate, string? WorkerPromptTemplate) result =
            await dbContext.Set<GlobalSettings>()
                .AsNoTracking()
                .Select(s => ValueTuple.Create(s.SystemPromptTemplate, s.WorkerPromptTemplate))
                .FirstOrDefaultAsync(cancellationToken);

        return result;
    }

    public async Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => new DispatchPauseState(
                s.UsageLimitResetsAt,
                s.IsDispatchPaused,
                s.AutoResumeOnUsageReset))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new DispatchPauseState(null, false, true);
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

    public async Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
    {
        // WorkerImageConfiguration is stored as a JSON blob and cannot be projected
        // into a SQL column — the full entity must be loaded and the value read in memory.
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return settings?.WorkerImageConfiguration.InstallDocker ?? false;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(
        CancellationToken cancellationToken)
    {
        // WorkerImageConfiguration is stored as a JSON blob and cannot be projected
        // into a SQL column — the full entity must be loaded and the value read in memory.
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return (settings?.WorkerImageConfiguration ?? WorkerImageConfiguration.Default).ToBuildArgs();
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
