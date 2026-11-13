using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IGlobalSettingsQueries"/> whose dispatch-pause state holds only the given provider-host pauses; every
/// other value is <see cref="NullGlobalSettingsQueries"/>'s.
/// </summary>
public sealed class PausedHostsGlobalSettingsQueries(params PausedHost[] pausedHosts) : IGlobalSettingsQueries
{
    public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
        => Task.FromResult<GlobalSettingsSummary?>(null);

    public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
        => Task.FromResult(int.MaxValue);

    public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);


    public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
        => Task.FromResult(new DispatchPauseState(null, false, false, pausedHosts));

    public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult(ImageBuildStatus.Idle);

    public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);


    public Task<bool> GetWorkerImageInstallsDotnetAsync(CancellationToken cancellationToken)

        => Task.FromResult(false);

    public Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken)
        => Task.FromResult("foundry-worker:local");

    public Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    public Task<WorkerResourceLimitsSnapshot> GetWorkerResourceLimitsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new WorkerResourceLimitsSnapshot(8192, 2.0, 512));

    public Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken)

        => Task.FromResult(new ClaudeCodeSettingsSnapshot([]));

    public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
