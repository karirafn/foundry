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

    /// <summary>How long a posted question waits without input before its one reminder (ADR 0096).</summary>
    Task<TimeSpan> GetAwaitingInputReminderDelayAsync(CancellationToken cancellationToken);

    Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken);

    Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken);

    Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken);

    Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken);

    /// <summary>Whether full-capability runs get the nested rootless Docker daemon and its allowances (ADR 0110).</summary>
    Task<bool> GetNestedDockerEnabledAsync(CancellationToken cancellationToken);

    Task<WorkerResourceLimitsSnapshot> GetWorkerResourceLimitsAsync(CancellationToken cancellationToken);

    Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken);
}
