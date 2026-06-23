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
        // Full entity load is required here: AuthMode uses a ValueConverter that
        // applies decrypt + JSON deserialization, which cannot be expressed as a
        // SQL projection. EF Core cannot translate the converter into a SELECT.
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

    public async Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken)
    {
        int? value = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => (int?)s.DefaultCooldownMinutes)
            .FirstOrDefaultAsync(cancellationToken);

        return value is > 0 ? value.Value : GlobalSettings.DefaultCooldownMinutesValue;
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

        return settings.ImageBuildState switch
        {
            ImageBuildState.Building => ImageBuildStatus.Building,
            ImageBuildState.Failed => ImageBuildStatus.Failed,
            _ => ImageBuildStatus.Idle,
        };
    }
}
