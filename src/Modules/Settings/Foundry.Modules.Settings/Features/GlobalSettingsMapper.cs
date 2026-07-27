using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

namespace Foundry.Modules.Settings.Features;

internal static class GlobalSettingsMapper
{
    internal static GlobalSettingsSummary ToSummary(GlobalSettings settings)
    {
        Contracts.ImageBuildStatus status = settings.ImageBuildState switch
        {
            ImageBuildState.Building => Contracts.ImageBuildStatus.Building,
            ImageBuildState.Failed => Contracts.ImageBuildStatus.Failed,
            _ => Contracts.ImageBuildStatus.Idle,
        };

        string? lastError = settings.ImageBuildState is ImageBuildState.Failed failed
            ? failed.ErrorTail
            : null;

        return new GlobalSettingsSummary(
            settings.MaxConcurrent,
            settings.TimeoutMinutes,
            settings.SystemPromptTemplate,
            settings.WorkerPromptTemplate,
            settings.UsageLimitResetsAt,
            settings.IsDispatchPaused,
            settings.AutoResumeOnUsageReset,
            settings.DefaultCooldownMinutes,
            settings.WorkerImageConfiguration.InstallDotnet,
            settings.WorkerImageConfiguration.InstallAngular,
            settings.WorkerImageConfiguration.InstallGlab,
            settings.WorkerImageConfiguration.InstallGh,
            settings.WorkerImageConfiguration.InstallChromium,
            settings.WorkerImageConfiguration.InstallDocker,
            status,
            lastError,
            settings.LastImageBuiltAt is not null);
    }
}
