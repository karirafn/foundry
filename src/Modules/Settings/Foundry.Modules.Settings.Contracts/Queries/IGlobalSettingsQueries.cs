namespace Foundry.Modules.Settings.Contracts.Queries;

public interface IGlobalSettingsQueries
{
    Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken);

    Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken);

    Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken);

    Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken);

    Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken);

    /// <summary>How long a posted question waits without input before its one reminder (ADR 0096).</summary>
    Task<TimeSpan> GetAwaitingInputReminderDelayAsync(CancellationToken cancellationToken);

    /// <summary>Whether dispatch resumes on its own once a usage-limit pause reaches its reset time.</summary>
    Task<bool> GetAutoResumeOnUsageResetAsync(CancellationToken cancellationToken);

    Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken);

    Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken);

    Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken);

    /// <summary>Whether full-capability runs get the nested rootless Docker daemon and its allowances (ADR 0110).</summary>
    Task<bool> GetNestedDockerEnabledAsync(CancellationToken cancellationToken);

    Task<WorkerResourceLimitsSnapshot> GetWorkerResourceLimitsAsync(CancellationToken cancellationToken);

    Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken);
}
