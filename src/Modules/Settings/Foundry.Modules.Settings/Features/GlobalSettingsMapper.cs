using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;

namespace Foundry.Modules.Settings.Features;

internal static class GlobalSettingsMapper
{
    internal static GlobalSettingsSummary ToSummary(GlobalSettings settings)
    {
        AuthMode.OAuth? oauth = settings.AuthMode is AuthMode.OAuth o ? o : null;

        string authModeName = settings.AuthMode switch
        {
            AuthMode.ApiKey => "ApiKey",
            AuthMode.OAuth => "OAuth",
            _ => "Unknown",
        };

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
            authModeName,
            settings.MaxConcurrent,
            settings.TimeoutMinutes,
            oauth is not null && oauth.AccessToken.Length > 0,
            oauth is not null && oauth.RefreshToken.Length > 0,
            oauth?.ExpiresAt,
            oauth?.SubscriptionType,
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
            lastError);
    }
}
