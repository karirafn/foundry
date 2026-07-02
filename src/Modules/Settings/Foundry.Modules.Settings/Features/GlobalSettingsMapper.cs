using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;

namespace Foundry.Modules.Settings.Features;

internal static class GlobalSettingsMapper
{
    internal const string OAuthStatusNotConfigured = "NotConfigured";
    internal const string OAuthStatusPresent = "Present";
    internal const string OAuthStatusReLoginNeeded = "ReLoginNeeded";

    internal static GlobalSettingsSummary ToSummary(GlobalSettings settings, CredentialVolumeStatus? credentialStatus)
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

        string oauthStatus = ComputeOAuthStatus(settings, credentialStatus);
        DateTimeOffset? expiresAt = credentialStatus?.ExpiresAt;
        string? subscriptionType = credentialStatus?.SubscriptionType
            ?? (settings.AuthMode is AuthMode.OAuth oauth ? oauth.SubscriptionType : null);

        return new GlobalSettingsSummary(
            authModeName,
            settings.MaxConcurrent,
            settings.TimeoutMinutes,
            oauthStatus,
            expiresAt,
            subscriptionType,
            settings.OAuthAccountEmail,
            settings.OAuthAccountOrgName,
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

    private static string ComputeOAuthStatus(GlobalSettings settings, CredentialVolumeStatus? credentialStatus)
    {
        if (settings.AuthMode is not AuthMode.OAuth)
        {
            return OAuthStatusNotConfigured;
        }

        if (settings.AuthInvalidPause)
        {
            return OAuthStatusReLoginNeeded;
        }

        if (credentialStatus is null || !credentialStatus.Present)
        {
            return OAuthStatusReLoginNeeded;
        }

        return OAuthStatusPresent;
    }
}
