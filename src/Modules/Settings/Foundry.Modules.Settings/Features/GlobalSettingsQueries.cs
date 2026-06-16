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

        return settings is null ? null : GlobalSettingsMapper.ToSummary(settings);
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
            AuthMode.ApiKey apiKey => ("ANTHROPIC_API_KEY", apiKey.Key),
            AuthMode.OAuth oauth => ("CLAUDE_CODE_OAUTH_TOKEN", oauth.AccessToken),
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

    public async Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
        CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return (settings?.SystemPromptTemplate, settings?.WorkerPromptTemplate);
    }
}
