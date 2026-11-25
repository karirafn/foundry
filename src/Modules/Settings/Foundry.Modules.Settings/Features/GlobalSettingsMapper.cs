using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts.Queries;

namespace Foundry.Modules.Settings.Features;

internal static class GlobalSettingsMapper
{
    internal static async Task<GlobalSettingsSummary> ToSummaryAsync(
        GlobalSettings settings,
        IDispatchPauseQueries dispatchPauseQueries,
        int webhookPort,
        CancellationToken cancellationToken)
    {
        DispatchPauseState pauseState = await dispatchPauseQueries.GetDispatchPauseStateAsync(cancellationToken);
        return ToSummary(settings, pauseState, webhookPort);
    }

    internal static GlobalSettingsSummary ToSummary(GlobalSettings settings, DispatchPauseState pauseState, int webhookPort)
    {
        ImageBuildStatus status = settings.ImageBuildState.ToStatus();

        (string? lastError, DateTimeOffset? nextRetryAt, int attempt) = settings.ImageBuildState switch
        {
            ImageBuildState.Failed failed => (failed.ErrorTail, failed.NextRetryAt, failed.Attempt),
            ImageBuildState.ScheduledRebuildFailed scheduled => (scheduled.ErrorTail, scheduled.NextRetryAt, scheduled.Attempt),
            _ => ((string?)null, (DateTimeOffset?)null, 0),
        };

        return new GlobalSettingsSummary(
            settings.MaxConcurrent,
            settings.TimeoutMinutes,
            settings.ProbeIntervalMinutes,
            settings.PollIntervalSeconds,
            pauseState.UsageLimitResetsAt,
            pauseState.IsDispatchPaused,
            settings.AutoResumeOnUsageReset,
            settings.WorkerImageConfiguration.ImageTag,
            settings.WorkerImageConfiguration.BuildEnabled,
            settings.NestedDockerEnabled,
            status,
            lastError,
            settings.LastImageBuiltAt is not null,
            nextRetryAt,
            attempt,
            settings.AllowedProviderHosts,
            settings.WorkerResourceLimits.MemoryLimitMb,
            settings.WorkerResourceLimits.CpuLimit,
            settings.WorkerResourceLimits.PidsLimit,
            settings.ClaudeCodeSettings.AdditionalDenyRules,
            pauseState.PausedHosts,
            webhookPort,
            settings.WebhookPublicBaseUrl?.Value,
            settings.BackstopIntervalMinutes,
            settings.AwaitingInputReminderDelayHours,
            pauseState.PackageProxyPause,
            settings.LastImageBuiltAt);
    }
}
