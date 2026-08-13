using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

namespace Foundry.Modules.Settings.Features;

internal static class GlobalSettingsMapper
{
    internal static GlobalSettingsSummary ToSummary(GlobalSettings settings)
    {
        // Capture the failed state once; derive both status and failure fields from a single pattern.
        ImageBuildState.Failed? failedState = settings.ImageBuildState as ImageBuildState.Failed;

        ImageBuildStatus status = settings.ImageBuildState.ToStatus();

        string? lastError = failedState?.ErrorTail;
        DateTimeOffset? nextRetryAt = failedState?.NextRetryAt;
        int attempt = failedState?.Attempt ?? 0;

        return new GlobalSettingsSummary(
            settings.MaxConcurrent,
            settings.TimeoutMinutes,
            settings.ProbeIntervalMinutes,
            settings.SystemPromptTemplate,
            settings.WorkerPromptTemplate,
            settings.UsageLimitResetsAt,
            settings.IsDispatchPaused,
            settings.AutoResumeOnUsageReset,
            settings.WorkerImageConfiguration.InstallDotnet,
            settings.WorkerImageConfiguration.InstallAngular,
            settings.WorkerImageConfiguration.InstallGlab,
            settings.WorkerImageConfiguration.InstallGh,
            settings.WorkerImageConfiguration.InstallChromium,
            settings.WorkerImageConfiguration.InstallDocker,
            status,
            lastError,
            settings.LastImageBuiltAt is not null,
            nextRetryAt,
            attempt);
    }
}
