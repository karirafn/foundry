namespace Foundry.Modules.Settings.Contracts.Queries;

/// <summary>
/// Every dispatch-pause reason. <see cref="IsDispatchPaused"/> and <see cref="UsageLimitResetsAt"/> hold all dispatch;
/// each of <see cref="PausedHosts"/> holds only the repositories on its host.
/// </summary>
public sealed record DispatchPauseState(
    DateTimeOffset? UsageLimitResetsAt,
    bool IsDispatchPaused,
    bool AutoResumeOnUsageReset,
    IReadOnlyList<PausedHost> PausedHosts);

public interface IGlobalSettingsQueries
{
    Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken);

    Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken);

    Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken);

    Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken);

    Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken);

    Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken);

    Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken);

    Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken);

    Task<bool> GetWorkerImageInstallsDotnetAsync(CancellationToken cancellationToken);

    Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken);

    Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken);

    Task<WorkerResourceLimitsSnapshot> GetWorkerResourceLimitsAsync(CancellationToken cancellationToken);

    Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken);
}
