using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>
/// Configurable stub of <see cref="IGlobalSettingsQueries"/> for unit tests that need
/// an explicit global concurrency cap. Use <see cref="NullGlobalSettingsQueries"/> instead
/// when a test does not depend on the global cap (it returns <see cref="int.MaxValue"/>).
/// </summary>
public sealed class FixedCapGlobalSettingsQueries(int maxConcurrent) : IGlobalSettingsQueries
{
    public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
        => Task.FromResult<GlobalSettingsSummary?>(null);

    public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
        => Task.FromResult(maxConcurrent);

    public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<(string SystemPromptTemplate, string WorkerPromptTemplate, string BranchNamingInstruction)> GetPromptTemplatesAsync(
        CancellationToken cancellationToken)
        => Task.FromResult(("", "", ""));

    public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
        => Task.FromResult(new DispatchPauseState(null, false, false));

    public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult(ImageBuildStatus.Idle);

    public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken)
        => Task.FromResult("foundry-worker:local");

    public Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
