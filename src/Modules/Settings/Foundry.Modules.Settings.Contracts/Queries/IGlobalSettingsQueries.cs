namespace Foundry.Modules.Settings.Contracts.Queries;

public sealed record DispatchPauseState(
    DateTimeOffset? UsageLimitResetsAt,
    bool IsDispatchPaused,
    bool AutoResumeOnUsageReset);

public interface IGlobalSettingsQueries
{
    Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken);

    Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken);

    Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken);

    Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken);

    Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
        CancellationToken cancellationToken);

    Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken);

    Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken);

    Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken);
}
