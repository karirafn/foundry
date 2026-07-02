namespace Foundry.Modules.Settings.Contracts.Queries;

public sealed record DispatchPauseState(
    DateTimeOffset? UsageLimitResetsAt,
    bool IsDispatchPaused,
    bool AutoResumeOnUsageReset,
    bool AuthInvalidPause = false);

public interface IGlobalSettingsQueries
{
    Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken);

    Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(CancellationToken cancellationToken);

    Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken);

    Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken);

    Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
        CancellationToken cancellationToken);

    Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken);

    Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken);

    Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken);

    Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken);
}
