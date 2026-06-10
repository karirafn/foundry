using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal sealed class GlobalSettingsQueries(DbContext dbContext) : IGlobalSettingsQueries
{
    public async Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return settings is null ? null : MapToSummary(settings);
    }

    public async Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(
        CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            return null;
        }

        return settings.AuthMode switch
        {
            AuthMode.ApiKey apiKey => ("ANTHROPIC_API_KEY", apiKey.EncryptedKey),
            AuthMode.OAuth oauth => ("ANTHROPIC_AUTH_TOKEN", oauth.AccessToken),
            _ => null,
        };
    }

    public async Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return settings?.MaxConcurrent ?? GlobalSettings.DefaultMaxConcurrent;
    }

    public async Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return settings?.TimeoutMinutes ?? GlobalSettings.DefaultTimeoutMinutes;
    }

    private static GlobalSettingsSummary MapToSummary(GlobalSettings settings)
    {
        AuthMode.OAuth? oauth = settings.AuthMode as AuthMode.OAuth;

        string authModeName = settings.AuthMode switch
        {
            AuthMode.ApiKey => "ApiKey",
            AuthMode.OAuth => "OAuth",
            _ => "Unknown",
        };

        return new GlobalSettingsSummary(
            authModeName,
            settings.MaxConcurrent,
            settings.TimeoutMinutes,
            oauth is not null && oauth.AccessToken.Length > 0,
            oauth is not null && oauth.RefreshToken.Length > 0,
            oauth?.ExpiresAt,
            oauth?.SubscriptionType);
    }
}
