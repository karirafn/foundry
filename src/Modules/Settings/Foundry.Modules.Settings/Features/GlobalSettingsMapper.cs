using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;

namespace Foundry.Modules.Settings.Features;

internal static class GlobalSettingsMapper
{
    internal static GlobalSettingsSummary ToSummary(GlobalSettings settings)
    {
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

        // TEMPORARY: AccessTokenPresent, RefreshTokenPresent, and ExpiresAt are emitted as
        // false/null for OAuth mode. Step 6 replaces these with volume-derived status fields.
        string? subscriptionType = settings.AuthMode is AuthMode.OAuth oauth
            ? oauth.SubscriptionType
            : null;

        return new GlobalSettingsSummary(
            authModeName,
            settings.MaxConcurrent,
            settings.TimeoutMinutes,
            AccessTokenPresent: false,
            RefreshTokenPresent: false,
            ExpiresAt: null,
            subscriptionType,
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
